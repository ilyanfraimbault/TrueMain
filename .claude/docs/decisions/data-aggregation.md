# Aggregates, retention and the schema

Part of the [decision log](../decisions.md). Format: **Decision** — why — `source`.

**Champion aggregates are replace-by-scope on *live* patches only. Old patches are frozen and must never be wiped.**
Retention deletes `match_participants` past `RetainedPatchCount` (default 2). The cleanup set originally
spanned all patches, so every aggregation run deleted old-patch scopes and could only rebuild those still
inside the retention window — permanently destroying history, since no source rows remain. The aggregate *is*
the history for that patch; the accepted trade-off is that a frozen patch can never be recomputed — #466,
reaffirmed by #606 and #694.

**"Live" is the retained-patch window, not "has at least one match left".** Old games keep being ingested
long after their patch retired, and retention only sweeps them on its next pass, so the weaker definition let
a retired patch flicker back to life and had its whole history rebuilt over those few stragglers — #466 held
only for patches nobody backfilled. The window is now each platform's `RetainedPatchCount` most recent
patches, ranked by the recency of their newest *game*, shared with `MatchDataRetentionProcess` through
`RetainedPatchWindow` so the two cannot drift. One snapshot per run feeds both the cleanup set and the source
rows, which is also what makes the scope insert collision-free: a pair entering the window mid-run waits for
the next run instead of being rebuilt without its cleanup — #1549.

**Aggregate retention is opt-in per environment: `AggregateRetainedPatchCount` defaults to 0 (frozen forever); preprod sets 2.**
The freeze is right for production history but preprod must stay tiny — #711. *Superseded for production by the
two-level retention below (2026-10-06, #1467); stays in force until its implementation ships.*

**Build aggregates live for two patches; the long tail keeps a light per-patch record (2026-10-06).** Nobody reads
a build from four patches ago — items have been reworked and the build misleads rather than merely ages — while those
frozen aggregates keep costing storage and aggregation time on a stack that has hit disk-full (#680) and
aggregation-timeout (#600, #632) incidents. So two levels: the full aggregates (everything the champion page renders)
for the current and the previous patch only, and per patch a light record — win, pick and ban rate per champion ×
position × elo bracket, a few thousand rows — which feeds "Trend by patch" and honours its rank filter. The light row
of a patch is written before any of its full aggregates is dropped (never purge first and backfill later: the
history would be gone), and patch N-2 is purged only once patch N has enough matches to be live. This reverses the
production half of #466/#711 ("old patches are frozen and must never be wiped") on purpose — #1467.

**Timeline snapshots keep the canonical marks {5, 10, 15, 20, 30} only; retention prunes the legacy per-minute grid.**
The dense per-minute grid grew to ~13 GB / 55.6M rows for exactly one consumer (power spikes); every other
reader uses only those five marks. It was first pruned once a match was powerspike-aggregated (#772, #694);
with power spikes removed, ingestion writes the five marks only and retention prunes every timeline-ingested
match still carrying the old grid, flagged `TimelineSnapshotsPruned` so it is scanned once — #1599.

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

**Champion profiles are measured from the champion's own games, never labelled by hand.**
`champion_profile_stats` (#1449) answers "what does this champion do" — how its damage splits by type, how much
it heals, shields and CCs, how much of its team's damage it absorbs, how its lane goes at 10/15, which item
archetypes it completes, whether it is ranged — as additive sums per `(champion, position, patch)` folded by
`ChampionProfileAggregationProcess` from the #1448 context columns, the timeline snapshots and the final
inventory. A hand-kept list ("Malphite is a tank", "Lux is AP") was rejected: it encodes one opinion, goes stale
at every rework, and would have to be maintained for ~170 champions across five positions. The profiles are the
dictionary the situational item fold (#1450) uses to qualify a draft, so their honesty is what the whole feature
rests on. Three consequences: the fold takes the **full participant pool** (a profile describes the champion, not
its mains — only remakes and non-canonical positions are excluded); **only participants carrying the context
columns count**, so the flag ships `false` and the pre-#1448 history is flagged without diluting anything; and the
**ranged flag is the one static attribute**, read from Data Dragon and `COALESCE`d on write so a CDN outage never
blanks it, while an item-metadata outage aborts the run — flagging a match without its
archetypes would lose them for good. Shares, means and per-minute rates are read-time arithmetic over the sums;
readers apply their own games floor — an additive fold cannot know a row's final count — #1449.

**A champion's damage profile is read through the fold's snapshot, split by build in a sibling table, and an
unprofiled champion gets a class, never a share (2026-10-05).** `GET /champions/damage-profiles` (#1905) serves the
profile shares for the desktop draft's team damage bars. Three calls:
- **One snapshot rule.** The endpoint, the item-context fold and the next-item read resolve profiles through
  `ChampionProfileSnapshotRules` (lookback 2 patches, floor 100 games — the ingestor options default to it), so a
  bar the player sees and the axis that drives the build advice cannot disagree; a test pins that a client
  rebuilding a team's magic share from the payload lands in the same `EnemyMagicDamage` bucket as
  `DraftAxisEvaluator`.
- **The build split is a sibling table, not a key column.** `champion_damage_profile_stats` holds the damage sums
  per `(champion, position, patch, archetype)`, folded in the profile's pass under the same `ProfileAggregated`
  gate. Adding the archetype to `champion_profile_stats`' key would multiply every row the item-context fold
  reads and change what a profile means; the blended profile stays the draft-time expectation. The archetype is
  the one the final inventory leans on — counted per item, so one hybrid item does not flip an AD build to AP;
  ties go AP > crit > armour-pen > on-hit > tank; `None` when nothing is classified — stored as text (a
  dimension read in ad-hoc SQL). **No backfill**: resetting `ProfileAggregated` would fold every match into the
  profile a second time, and a per-match flag of its own buys one retained window; the table fills forward and
  a build's share is over the table's own games, so a partly covered patch is a smaller sample, not a skewed
  one. `flexDamage` = two builds with opposite dominant damage types each holding ≥ 20% of the games — one cut for
  every champion, not a per-champion list.
- **Fallback = a class, never a percentage.** Preference: measured at the lane → measured at the best-covered lane
  → Data Dragon's `info.attack` / `info.magic` ratings (gap ≥ 3 names a type, else `mixed`) → nothing.
  CommunityDragon's `tacticalInfo.damageType` and Meraki were the alternatives: Data Dragon was already a
  dependency (the ranged flag) with a client, and both others are a new source for the same coarse answer.
  The ratings are hand-authored and old, so they never produce a share — #1905.

**A situation is only allowed to explain an item it could mechanically answer — the whitelist is the feature.**
`champion_item_context_stats` (#1450) measures an item's pick rate at each end of fifteen draft axes and keeps the
axes that move it. Over a patch's games plenty of unrelated draft features correlate with an item's pick rate, so
"significant" alone would eventually surface *"Zhonya's Hourglass, built against AD-heavy teams"* with a perfectly
good p-value, and one sentence like that costs more credibility than ten correct ones earn. Eligibility is
therefore **derived from the item's own CommunityDragon categories**, not from a maintained list: magic resistance
may answer magic damage, armour may answer physical damage / crit / lethality / melee, tenacity may answer crowd
control, Grievous Wounds may answer sustain, penetration may answer a frontline, health *with no resistance behind
it* may answer either damage type. Non-eligible pairs are not merely hidden — they are never counted, which is
also what keeps the table at the scale of the matchup pre-aggregation. Three floors together decide a finding
(games in both buckets, absolute lift, two-proportion significance): a large sample makes a two-point gap
significant and a spectacular gap over ten games is noise, so no single floor is enough — #1450.

**An item-context verdict is about an edge of the build tree, not about an item.**
The first grain measured an item against every game of its slot — "how often does this champion ever finish this
item". That is not a decision anybody makes, and it is confounded by game length: being ahead at 15 minutes means
a longer game and more gold, so *every* late item is completed more often. Measured on production at the time:
851 of 980 findings were `OwnGoldLeadAt15`, 828 of them at the `High` end, and it was the top finding 844 times —
nine situational cards in ten said "when ahead at 15 min". Since #1496 every counter and every verdict is scoped
to the branch the step was taken on, `(slot, parentItem → item)`, and a branch's denominator is the games that
reached the parent **and completed a further item**. The siblings then partition the branch, so no axis can lift
all of them at once and the confounder dies mechanically instead of being suppressed. A step nothing competes
with — no sibling clears `MinAlternativeShare`, the same 10% the build tree prunes its children at — is `Core`
whatever its rate: there was no decision to explain. Draft-time findings are ordered ahead of the gold lead,
whose bands were also moved from ±300 to the measured deciles (±1 800 over 194 510 lanes), so `High` means
snowballing rather than "the game lasted long enough to buy things" — #1496, #1450.

**Verdicts are derived and rebuilt; the counters are additive and folded once per match.**
The same process does both, and the split is the point. The counters follow the house rule (one fold per match,
flagged on `matches`, frozen patches freeze). The verdicts are recomputed wholesale for the scopes a run touched,
which is what makes the API read a lookup with no statistics in it — the requirement the table exists for — and
means a re-tuned threshold costs a verdict rebuild rather than a re-fold of the retained history — #1450.

**A thin bucket widens backwards through patches, both ends together, and says so.**
A situation is much rarer than a champion, so a bucket can be thin on a patch the champion itself is well covered
on; a per-patch floor alone would starve exactly the axes that matter most. The builder folds previous patches
into **both** ends of an axis until the floor is met (never one — two rates read off different windows are not
comparable), up to `MaxPatchLookback`, and records the window per finding so the sentence can say "over the last
three patches". The class and the pick rate are never widened: those describe the served patch alone — #1450.

**The item context carries no elo dimension, unlike every sibling aggregate.**
Splitting by rank multiplies the rows by eleven *and* divides every bucket's sample by the same factor, which
starves the buckets the feature rests on. The verdicts therefore describe every rank together and the read says
so, rather than letting a card look rank-scoped beside panels that are — #1450.

**Known gap: the lane opponent is not held out of a team-level axis.**
An axis could in principle be carried by one recurring lane opponent rather than by the situation. Testing it needs
the opponent as a dimension of the counters, which multiplies them by the number of opponents a champion meets
(~70 measured on production) — prohibitive at this grain, so it is deliberately not done and tracked in #1462.
What blocks the absurd cases meanwhile is the mechanical whitelist above — #1450.

**The next-item model predicts the mains' choice from the item-context counters; it is not a win-rate argmax.**
The desktop app asks on every purchase, so the read must be a lookup: the fold derives, beside the verdicts,
`champion_next_item_terms` — per build/boots branch, each candidate's base log-share and, per axis bucket, the
log-ratio that bucket shifts it by, shrunk towards zero by 50 pseudo-games so a thin bucket costs nothing and no
floor has to be right. A game's score is base + the terms of the buckets it sits in (naive Bayes), normalised
over the candidates it does not already hold. An item's win rate is mostly the game state it is bought in, so it is
returned as a fact, never as the ranking key. Terms read their shift over the served patch plus the lookback
window and their base share on the served patch alone (widened only below 30 branch games) — #1749.

**The model shipped on a held-out measurement, and the measurement says where its value is.** Trained on the
16.16–16.18 counters, replayed on 54 243 real decisions of 16.19 (30 most-played champion-lanes, the mains'
games of one day; `backend/Tools/NextItemEvaluation`): boots top-1 61.8 % against 60.4 % for "the branch's most
common child", and where the two disagree the model is right 400 times to the baseline's 215; legendaries 55.0 %
against 54.9 %, a coin flip where they disagree (534 to 509). The draft composition moves the mains' boots, barely
their items — what the enemies have *built* (#1750) is the lever the in-game panel needs. Grouping correlated axes
so each group spoke once was measured too and did no better than plain summing, so the model sums — #1749.

**No pick+ban "presence" figure, despite it being standard elsewhere.**
Pick rate's denominator is tracked mains' games at a lane; ban rate's is every observed match. The two are not
addable, and a presence number computed from them would be arithmetic without meaning. Offering a meta-wide
pick rate purely to make them addable was rejected: it would put two different pick rates on the same page —
#920.

**A dimension's identity is enforced by the schema, not repaired afterwards.**
`champion_dim_rune_pages` kept the two secondary perks in the player's selection order, so one page existed
as `(8451, 8444)` and `(8444, 8451)`. The 11-column unique index does not catch a permutation, so the page's
games and wins were split across both rows — roughly halving its displayed pick rate and distorting the
top-N. It reached 48% of the dimension (20 370 pairs) before anyone noticed, because the two rows render
pixel-identically (#911). `champion_dim_starter_items` failed the same way one level up: its unique key was a
string the application built by joining the basket in *price* order, so a re-priced starter — or an item
whose metadata went missing, which prices it at 0 — re-keyed a basket already stored, and 17 baskets sat
split in production. Both were first answered with a repair (a pipeline step, a data migration), and both
came back, because a repair leaves the state reachable.

Since #1418 the guarantee is in the schema: a UNIQUE index over each dimension's *canonical expression*
(`LEAST`/`GREATEST` on an order-insensitive pair), a CHECK on the two dimensions whose canonical form is a
column order so a writer regression fails loudly, and for starter baskets a **stored generated column** —
Postgres derives the key from the basket itself, ids ascending, so no writer computes it. Identity may not
depend on data that changes, and item prices change. `champion_dim_builds` and `champion_dim_skill_orders`
stay on their plain column index: there the order *is* the datum. The ingestor's dimension resolver inserts
with `ON CONFLICT DO NOTHING` and re-reads, so a disagreement between its normalisation and the schema's
costs a re-read instead of a failed aggregation run; `RunePageDeduplicationProcess` was deleted with the
merge folded into the same migration that adds the constraints — #1418, #911, #924.

**A champion stat row's invariants are write-time CHECKs, added `NOT VALID`; the one lane sentinel is pinned, not
made `NULL`.** Every table with an `int Games` and an `int Wins` carries `Wins <= Games`, applied by convention in
`Data/DataQuality/ChampionStatInvariants.cs` so a new stat table gets it from its first migration;
`champion_matchup_stats` adds `LaneGames <= Games`; the fold lane columns (matchups, opponents, synergies and their
baselines, profiles) must be one of the five canonical lanes, and an opponent or partner a real champion (`> 0`).
The reason is the #1418 one moved to the counters: the folds are additive and a frozen patch is never recomputed
(#466), so a writer regression is permanent the moment its patch freezes, and only a constraint fails it on the
batch that would have written it. **`NOT VALID`, deliberately:** every insert and update is checked from the
migration on, but the rows already written are not scanned — frozen patches hold rows folded under older rules
(positions before #1087, lane counters folded off their own flag before #1445) that nothing can rebuild, and a
failed `VALIDATE` would stop the deploy carrying it. The live window's rows are all folded under the current
rules, so the folds never update a row the check refuses. **The sentinel:** `champion_aggregate_scopes.Position`
is the only lane column with a "no lane" value in its meaning; it stays non-nullable and the check pins `''` as
its single spelling (it had been written `''` and `' '`). Turning it into `NULL` would touch every reader of the
column for no gain the check does not already give. `match_participants` is left out: its `TeamPosition` is Riot's
raw value and its `elo_bracket` is the rank stamp #1367 owns — #1365.

**Rank snapshots are capped at one row per account per UTC day (DB-level unique index).**
Intra-day LP granularity has no consumer. Accepted: rank history, match detail and the "nearest snapshot" elo
resolvers are day-precision — #907.

**`(GameName, TagLine, PlatformId)` on `riot_accounts` is a plain, NON-unique index. PUUID is the only real identity.**
A Riot ID is mutable and recyclable, so a stale row and a freshly renamed row legitimately collide. The unique
constraint made one collision roll back the whole `AccountRefresh` batch, which then reselected and failed
forever — the process was dead from 2026-07-26 — #901 / PR #902.

**PUUID indexing is intentional — do not propose dropping it or migrating to `RiotAccountId`-only.**
Standing instruction from the project owner. Related chores (#123 LZ4 TOAST, #124 perk-selection PK) are
separate.

**`match_participants."ItemEvents"` / `"SkillEvents"` are TOAST-compressed with LZ4, not the cluster default PGLZ (2026-10-06).**
The two jsonb event payloads are the bulk of the table; LZ4 compresses JSON smaller and decompresses faster.
Per column, never cluster-wide `default_toast_compression`. Set in the model (`UseCompressionMethod("lz4")`) but
applied by raw SQL in the migration: Npgsql's generated `SET COMPRESSION` lacks its `;` and breaks the idempotent
script. `SET COMPRESSION` only affects new writes; the existing rows are rewritten by the `match_participants`
repack (#1946), never by a migration — #123.

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
rollout's `migrate` job like every other migration (`docs/production-migrations.md`, #598). The
indexes are ordinary `CREATE INDEX`, not `CONCURRENTLY`: the rewrite already holds the strongest lock there
is, so concurrency would buy nothing and cost the ability to run inside the script's transaction.

## A final inventory is slots 0–5 plus the role-bound slot, never the trinket (2026-09-17)

**Every build derivation reads a participant's end-of-game inventory through `Data/BuildFacts/FinalInventory`:
the six inventory slots and Riot's `roleBoundItem`, stored as `match_participants."RoleBoundItemId"`.**
`item6` is the trinket slot and nothing else (ward, lens, Farsight, the odd Eye of the Herald). It used to be
handed to the resolvers as a seventh "final item" and kept out of builds only by an id filter on the
trinkets. Leaving it out of the input makes a trinket in a build impossible rather than merely filtered.

The role-bound slot exists since the role quests. It **always** holds a bot laner's boots, quest done or not
(8,127 of 8,378 bot-lane rows on preprod, 2026-09-17; the other roles keep theirs in slots 0–5), so an ADC
can end on six items *and* boots. The other roles get their quest reward there (not in the store, so no
build picks it up), and a support gets a Control Ward (a consumable, filtered the same way). Reading the slot
is what lets the boots fallback and the profile fold's item archetypes see those boots.

**The column is nullable: `NULL` means "never recorded", 0 means "empty".** Rows ingested before the column
existed lost a bot laner's boots entirely, since they are in neither slots 0–5 nor the trinket.
`MatchRoleBoundItemBackfillProcess` recovers them from the stored item timeline: the last pair bought, or the
last pair seen when none was bought (a rune's boots). On preprod's new rows this matched Riot's value for 99 %
of bot laners, and every miss was that rune's boots, which the fallback covers. Re-fetching from Riot was
rejected: it spends the match-v5 budget on ~1M rows to recover what the row already holds. The other roles'
legacy rows stay `NULL` and read as empty, because their quest reward appears nowhere else. The column was
dropped and re-added rather than altered, so no row was rewritten on a 35 GB table.

**The match row and the scoreboard draw the inventory the way the game does** (`match/MatchItemGrid.vue`):
slots 0–5 as a 3×2 grid in slot order, then the trinket over the role-bound slot. #1607 pulled the boots out
of the grid into that column, thinking a seventh item came through the inventory slots. That left a gap in
nearly every grid and made the trinket read as one of the six items — `#1612`.

## The pace benchmark is a per-minute histogram folded from the timeline in memory, never a grid (2026-10-05)

**`pace_benchmark_stats` holds, per (patch, tier, position, minute, metric), a fixed-width histogram of a laner's
cumulative CS and gold earned — and nothing per participant.** The per-minute grid was removed on purpose (#772,
#1599), so the values are read off the match-v5 timeline while `TimelineIngestionService` holds it and only the bin
counts are written, in the same transaction, behind `matches.PaceBenchmarkAggregated`. That flag is flipped by a
conditional `UPDATE … WHERE NOT … RETURNING`, so two accounts ingesting one match cannot both count it; a match whose
timeline was ingested before the fold shipped is never folded — its minutes are gone. Histograms rather than sums,
because a mean cannot say where a player stands; the bins stay additive (`ON CONFLICT … + EXCLUDED`), and the
quartiles are read-time arithmetic (`Core/Lol/Pace/PaceHistogram.cs`, 5 CS and 200 gold a bin, interpolated inside
it). **The whole lobby is counted at the tracked account's tier at game time** (`EloBracketResolver`, the lower
median when several tracked accounts disagree); counting only tracked rows would benchmark mains, which the product
owner excludes. A lobby with no ranked tracked account, a remake or a minute the game did not reach adds nothing.
The read pools the newest three patches (pace barely moves between patches, and pooling fills the thin tiers) and
serves no quartile under 50 samples. The table joins `AggregateRetention` — #1912.

