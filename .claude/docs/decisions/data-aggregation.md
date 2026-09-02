# Aggregates, retention and the schema

Part of the [decision log](../decisions.md). Format: **Decision** — why — `source`.

**Champion aggregates are replace-by-scope on *live* patches only. Old patches are frozen and must never be wiped.**
Retention deletes `match_participants` past `RetainedPatchCount` (default 2). The cleanup set originally
spanned all patches, so every aggregation run deleted old-patch scopes and could only rebuild those still
inside the retention window — permanently destroying history, since no source rows remain. The aggregate *is*
the history for that patch; the accepted trade-off is that a frozen patch can never be recomputed — #466,
reaffirmed by #606 and #694.

**Aggregate retention is opt-in per environment: `AggregateRetainedPatchCount` defaults to 0 (frozen forever); preprod sets 2.**
The freeze is right for production history but preprod must stay tiny — #711.

**Timeline snapshots are pruned to the canonical marks {5, 10, 15, 20, 30} once a match is powerspike-aggregated.**
The dense per-minute grid grew to ~13 GB / 55.6M rows for exactly one consumer (power spikes); every other
reader uses only those five marks. Pre-aggregating power spikes first made the intermediate minutes
disposable — #772, #694.

**Aggregation is incremental per match, flagged on `matches`, never a full recompute.**
Full-recompute self-joins reached ~21.6 min per cycle on prod, making it 5.7× slower than preprod and
ingesting fewer games per day. The flag also dies with the match, so old-patch stats freeze naturally.
⚠️ A migration adding such a flag **must backfill existing rows to `true`** *when the aggregate it gates was
already populated by a full recompute* — otherwise the first incremental run double-counts — #811.
The mirror case: `matches.SynergyAggregated` (#922) ships `false` everywhere on purpose, because its tables are
created empty by the same migration and the whole retained history still has to be folded once. Read the flag's
question as "has this match already been counted *into this table*", not as a blanket rule.
Third case, `matches.BansAggregated` (#920): backfilled to `true` like #811 but for a different reason — the
source rows don't exist. Riot payloads aren't kept, so a match ingested before #920 has no `match_bans` rows at
all; folding it would add to the ban *denominator* while contributing no bans and deflate every rate.

**Ban rate is its own aggregate pair with a stored denominator, and `ALL` is a stored band — not a summed one.**
Bans come from every ingested match; `champion_aggregate_scopes` is per tracked account, so a ban count folded
in there would be a different population under the same roof. Hence `champion_ban_stats` (numerator) +
`ban_scope_totals` (denominator), folded together so a rate is always one cohort. The denominator is *stored*
because matches are retired after ~2 patches while aggregates are kept forever — once the matches are gone,
nothing else records how many there were. A match has no single elo band (`elo_bracket` is resolved per tracked
player), so it is counted once per band it touched **and** once in a stored `ALL` row: the bands overlap, so
summing them is not the match count. That is the one place `EloBracket.All` is persisted rather than being the
read-time union it is everywhere else — #920.

**No pick+ban "presence" figure, despite it being standard elsewhere.**
Pick rate's denominator is tracked mains' games at a lane; ban rate's is every observed match. The two are not
addable, and a presence number computed from them would be arithmetic without meaning. Offering a meta-wide
pick rate purely to make them addable was rejected: it would put two different pick rates on the same page —
#920.

**Dimension rows must be stored in a canonical order, not the order Riot reported them.**
`champion_dim_rune_pages` kept the two secondary perks in the player's selection order, so one page existed
as `(8451, 8444)` and `(8444, 8451)`. The 11-column unique index does not catch a permutation, so the page's
games and wins were split across both rows — roughly halving its displayed pick rate and distorting the
top-N. It reached 48% of the dimension (20 370 pairs) before anyone noticed, because the two rows render
pixel-identically. The primary tree was unaffected: there the selection index *is* the tree row. Ordering is
by perk id rather than tree row — the row is not stored backend-side and Riot assigns ids in row order
anyway. The backfill runs as a pipeline step, not a startup migration, because it rewrites hundreds of
thousands of pattern rows and prod migrates on startup. It also normalises pages that were never duplicated:
left in click order, the new canonical lookup would miss them and mint a second row, re-creating the bug —
#911.

**Rank snapshots are capped at one row per account per UTC day (DB-level unique index).**
Intra-day LP granularity has no consumer. Accepted: rank history, match detail and the "nearest snapshot" elo
resolvers are day-precision — #907.

**`(GameName, TagLine, PlatformId)` on `riot_accounts` is a plain, NON-unique index. PUUID is the only real identity.**
A Riot ID is mutable and recyclable, so a stale row and a freshly renamed row legitimately collide. The unique
constraint made one collision roll back the whole `AccountRefresh` batch, which then reselected and failed
forever — the process was dead from 2026-07-26 — #901 / PR #902.

**PUUID indexing is intentional — do not propose dropping it or migrating to `RiotAccountId`-only.**
Standing instruction from the project owner. Related open chores (#123 LZ4 TOAST, #124 perk-selection PK)
are separate and still open.

**Pattern aggregates use a junction model (`champion_aggregate_patterns` + globally deduplicated `champion_dim_*`).**
This replaced both the original 23-column wide table (index maintenance on every column, a migration for every
new dimension) and the Sprint-5 per-scope dim tables, which had *lost cross-dimension correlation* — you could
not ask "when this player picks AD-crit Yasuo, what runes do they run?". Phase 6 restored it, exposed as
`GET /champions/{id}?buildId=` — `docs/phase-5-data-split-rfc.md`, `docs/phase-6-pattern-junction-rfc.md`.

## The patch is a column on `matches`, not a `LIKE` prefix over `GameVersion` (2026-09-02)

Every champion read narrows to one patch, and until #1368 every one of them did it the same way:
`EF.Functions.Like(m.GameVersion, '16.17.%')`. `PatchFilter`'s own comment said the quiet part out loud —
that predicate is **never index-assisted**. There is no index on `GameVersion`, and Postgres only turns a
`LIKE` prefix into a range scan for a *literal* pattern under a `text_pattern_ops` index, never for a
parameter. With `max_parallel_workers_per_gather = 0` (#589, and it stays off), each champion read was a
single-threaded scan of `matches` joined to `match_participants`. Measured cold on production on 2026-09-02:
roam 3.4–5.1 s, synergies ~2 s, the directory 2.1 s.

`matches."Patch"` is now a **stored generated column** holding the `major.minor` prefix, and the filter is a
plain equality on an indexed column. Two indexes, because there are two access shapes: `(Patch, QueueId)` for
the reads that filter one patch, and `(QueueId, Patch, PlatformId)` for the two writers that *enumerate*
patches — retention's live window and the pattern aggregation's live-key `DISTINCT`.

**Generated, not written by the ingestor.** The alternative was a plain column filled at insert time plus a
backfill, and it loses on the thing that matters: a second writer (a repair job, a manual `UPDATE`, a restored
dump) can put a row in `matches` whose `Patch` disagrees with its `GameVersion`, and the disagreement is
invisible — the row simply vanishes from a patch's numbers. `GENERATED ALWAYS AS (...) STORED` makes that
unrepresentable, and costs nothing at read time. Stored rather than virtual because Postgres cannot index a
virtual generated column, which is the entire point.

**The SQL expression is a transcription of `PatchVersion.TryParse(...).ToMajorMinor()`, deliberately.** Same
answer for the awkward inputs — empty segments dropped, segments trimmed, `16.04.5` → `16.4` because each
segment is re-rendered through `::int`, and **NULL** where the C# rule returns "not a patch". One divergence,
on purpose: segments are capped at nine digits, so a ten-digit major that `int.TryParse` would still accept
yields NULL here instead of an out-of-range cast that would fail the INSERT. The expression lives in
`MatchConfiguration.PatchComputedColumnSql`, and `MatchPatchColumnIntegrationTests` runs the two
implementations against each other on real Postgres — nothing else ties them together, and a column that
quietly disagrees with the C# rule just drops rows out of every champion read.

**Not a startup migration.** Adding a STORED column rewrites the table under `ACCESS EXCLUSIVE` and the two
index builds follow it; at ~274 k rows that is seconds, but it goes out of band through the
`migrate-preprod`/`migrate-prod` job like every other migration (`docs/production-migrations.md`, #598). The
indexes are ordinary `CREATE INDEX`, not `CONCURRENTLY`: the rewrite already holds the strongest lock there
is, so concurrency would buy nothing and cost the ability to run inside the script's transaction.
