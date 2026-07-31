# TrueMain — decision log

Settled product and architecture decisions **with their rationale**, so a session doesn't re-litigate them or
propose something a past incident already ruled out. Companion file: [features.md](features.md) (the *what*).

Format: **Decision** — why — `source`.

Last verified against `develop` on 2026-07-28.

---

## Product

**Stats are computed from *true mains* only, not from every game on a champion.**
Averaging all games drowns the specialist signal. A player is promoted to "true main" through a
games-vs-mastery investment signal. This is the thesis of the site — `README.md`.

**Ranked solo/duo (queue 420) is the only queue stored; match history is solo/duo-only by design.**
Prod Postgres reached 68 GB and filled the VPS disk (Mongo SIGSEGV mid-write, API and ingestor down).
Retention only pruned 420, so flex/normal/arena accumulated forever. Every aggregate and query service was
already hard-scoped to 420, so non-420 had no consumer except player match history — accepted — #680.

**Dedication score (0–100) is the signature metric, always scoped to one champion.**
LP measures skill, dedication measures *ownership* of a champion. Weights 0.45 commitment / 0.20 span /
0.20 volume / 0.15 recency. Unfiltered, it scores the player's signature champion; filtered by `championId`,
it scores that one — `docs/dedication-score.md`, #530.

**The `/truemains` leaderboard is strictly `IsMain=true`.**
The `EXISTS` used to short-circuit without champion/position filters, so freshly ingested un-analysed accounts
appeared on a page literally named "Truemains" — Challenger rows with no champion icons — #184.

**Leaderboard games/KDA/WR come from frozen aggregate scopes, not live `match_participants`.**
Retention deletes participants past 2 patches, which collapsed real players to "0 games". Accepted caveats:
counts cover main champions only, and KDA is understated on pre-migration frozen scopes that cannot be
backfilled — #719.

**Inactive mains are retired via champion-mastery `lastPlayTime`; intake favours depth over breadth.**
Dead mains sat at rank #1 with 0 games while burning match-v5 calls. Mastery-v4 is one call per account
versus pulling match history. Past a minimum coverage per champion, more games from real mains beats more
distinct mains. Rows are deactivated (`IsActive=false`), **never deleted**, so a returning player is not
rediscovered from scratch — #900.

**Candidate scoring is scarcity-weighted and the `IsMain` threshold is coverage-adaptive (0.20 → 0.12).**
Games per champion spread 68× (Ezreal 2039 vs Amumu 30) and the binding constraint was retention, not
discovery — Amumu had 23 validated candidates and 0 `IsMain` under a flat 20% floor. Scarcity fills the
bucket, the adaptive floor stops it leaking, and sub-0.20 mains are flagged `IsExtendedSample` so the UI can
label them honestly — #407.

**Thin samples degrade, they don't 404.**
A champion shown as a main on a profile dead-ended with "Not enough games" on its build page, because the two
views count different populations. The min-games floor became a patch-*preference*; 404 only when no
aggregate exists at all — #762.

**Champion timeline-leads ("Lead vs role opponent") was removed; matchups stayed.**
Not worth its maintenance cost; effort went to power spikes instead. `JobMode.MatchupLeadAggregationOnly` and
the `MatchupLeadAggregation` options section were deliberately **not renamed** — they are config-facing
(`INGESTOR_JOB_MODE`) and renaming risks a prod startup failure for no functional gain — #889.

**Power spikes are per-core-build bars anchored on events, not a time curve; bar height is excess acceleration.**
A blended cross-build answer is wrong (Botrk vs Kraken rushes behave differently). Winrate delta was
considered and rejected as confounded — completing a third item correlates with already winning — #890, #775.

**Matchup-scoped power spikes are a dimension on the aggregate, not a live recompute — because a live
recompute is impossible.**
The other matchup-filtered sections fold `match_participants` live (#923), so #957 was written assuming the
same. It cannot work: a spike is the second difference of the power curve on a ±3-minute window around an
arbitrary event minute, and retention prunes the dense per-minute grid to {5, 10, 15, 20, 30} the moment the
match is folded (#772). The window has nothing left to sit on. What made it cheap instead is that the spike is
*already* opponent-relative — the fold resolves the lane opponent to build the diff series and was simply
discarding their id. Recording it splits the grain exactly: every game belongs to one opponent, so the
unscoped read recovers its old numbers by summing across them, which is what it already did.
Two consequences, both deliberate. **Coverage is forward-only**: pre-#957 rows sit at `OpponentChampionId = 0`
and no filter can match them, so a matchup's spikes start empty and fill in patch by patch — the section says
so rather than blaming the matchup. And **retention rolls the split back up** when a patch leaves the live
window: without that, a 500-game build shredded into 40 opponent rows of ~12 would fall entirely under the
`PowerspikeEventMinGames` floor and the next cycle would delete the patch's spikes from the *unscoped* read
too. The baseline curve stays champion-wide on purpose — it corrects the global concavity of lead curves, and
recomputing it on a 4-game matchup would swap that correction for noise and subtract the signal from itself — #957.

**Champion synergy is observed *minus expected* win rate, never the raw pair win rate.**
Ranking pairings by raw win rate just re-prints the tier list — two strong champions win together because
they are strong. Expected is combined in log-odds space (`Core.Lol.Synergy.SynergyMath`) rather than by adding
percentages, which is unbounded (two 70% champions would "expect" 90%) — #922.

**The expected value needs two different baselines plus a cohort intercept — one baseline would bias every number.**
The queried side is a tracked truemain on their signature champion (win rate well above 50%); the partner side is
whoever happened to share the game (near the population mean). `champion_synergy_baseline_stats` therefore stores
`SELF` and `ALLY` rows separately, and each ally term is expressed relative to the cohort rate. Without that
intercept every extra teammate would shift the expectation up by a constant and *every* synergy would read
negative. Both baselines are folded by the same process, from the same matches, as the pair rows — deriving them
from another aggregate would compare cohorts that do not match — #922.

**Trios are computed on demand from a chosen pair; the triple space is never pre-aggregated.**
The pair grain is bounded by (champion × lane × partner × lane); adding a third champion and lane multiplies that
into a space that is almost entirely empty, and the few populated cells carry single-digit samples. The live query
narrows to the games the duo actually shared *before* touching the third dimension, so it stays bounded by the
pair's game count rather than the champion's — which matters under `max_parallel_workers_per_gather=0`. Accepted
consequence: the trio slice sees only the retention window while the duo slice reads an aggregate that also holds
frozen patches, so their game counts differ — `pairGames` is returned explicitly rather than reused from the duo
response — #922.

**Synergy carries three separate sample floors, and a thin sample yields no entry rather than a hedged one.**
`MinSynergyGames` (20) is deliberately above the matchup floor: synergy is a *difference* between two rates, so its
sampling error is the sum of theirs. `MinSynergyTrioGames` (12) is necessarily below it, since a trio's sample is a
subset of its duo's. `MinSynergyBaselineGames` (50) is the one the other two cannot provide — a pairing can clear
its own floor while a baseline is still a coin flip, and a noisy baseline produces a *confidently wrong* number,
not a noisy one. Below any of them the API returns no entry and the real game count, and the UI says which case it
hit — #922.

**Share cards degrade in three steps and never print a number the API did not return.**
An OG image is read as a screenshot of the page, so a filler `0%` is indistinguishable from a
measurement — the "no fabricated numbers" rule that governs the pages binds harder here, because the
reader has no page to check it against. Champion card: full stats when a `GET /champions` row exists →
portrait + name and *no numbers at all* when it doesn't → plain branded card when even DDragon is
unreachable. A null `banRate` (a patch before #920's ingestion) drops that one tile rather than zeroing
it, matching the em dash the directory prints. Truemain card: a missing ranked snapshot reads
"Unranked" (a real answer, unlike 0 LP), absent `wins`/`losses` drop the record line *and* the win-rate
tile, no classified main drops the champion block, and an unknown player falls all the way back to the
branded card — the page renders "not found" for the same input, so a profile-shaped preview would be a
lie. The dedication score is only printed when its `championId` matches the main shown beside it — #926.

**Share cards resolve their own data server-side instead of receiving it from the page.**
`nuxt-og-image` encodes the template props into the (signed) image URL, which is minted during SSR — but
both pages fetch `server: false` (the #149 hydration fix on champions, the deliberate no-cross-viewer-SSR
rule on profiles), so at that moment the page holds no numbers to hand over. The URL therefore carries
*identifiers only* and the templates resolve the real slice through `server/api/og/**` when a crawler
renders the image. The alternative — adding an SSR-enabled fetch to the pages purely to feed the card —
would put a backend round-trip on every human page view to serve the unfurl path. Accepted consequence:
the card's numbers are resolved at share time, so they can differ from a page the visitor left open —
both are real, an hour apart at most (the cache TTL) — #926.

**"Client-only fetch" has to be enforced on the *side*, not merely intended — an immediate watcher is not client-only.**
`useTruemainFetch` (profile / rank-history / matches) is hand-rolled refs precisely because these payloads are
per-viewer and must never enter shared SSR HTML, but its initial run hung off `watch(..., { immediate: true })`,
which fires during SSR too. It then *won* the race: the page render is meanwhile awaiting its two SSR-enabled
static lookups (rune tree + summoner spells, external DDragon/CDragon calls), so a local
`/api/truemains/{tag}/*` hit resolved first and the profile landed in the server-rendered markup — while the
client's first, hydration render always starts in the loading state. Vue reconciled skeletons against
rendered content, hit `insertBefore: node is not a child of this node`, then crashed on a null component in
the patch loop, and `/truemains/{nameTag}` sat in its skeletons **permanently** on any full page load
(client-side navigation was fine — it hydrates nothing). The initial run is now `onMounted`, which cannot run
on the server. Consequences worth knowing: the SSR markup for a profile is *always* the skeleton branch, and
the SSR `<title>` is the raw `Name-TAG` slug rather than `Name#TAG` — both are the price of the
no-cross-viewer-SSR rule, not oversights, and "fixing" either by SSR-ing the profile reintroduces the hang.
Disabling SSR on the route or timing out the fetch were both rejected: neither addresses the mismatch, and
the route is a primary, indexable one — #862.

**The activity grid's four modes read two different tables, and the response says so rather than reconciling them.**
`match_participants` carries a game's date but is hard-deleted past `RetainedPatchCount` (~2 patches);
`champion_aggregate_scopes` is frozen forever (#466) but its grain is (account, champion, patch). So the
game / day / week modes physically cannot show last season and the patch mode physically cannot show a day —
the modes are not four views of one dataset. Every series therefore ships its own `source`, `scope`,
`retentionBounded` and coverage range, and the patch series is champion-scoped to the signature champion.
Originally the UI printed a standing coverage line built from those fields; #959 replaced it with a per-cell
hover tooltip and dropped the line, on the reasoning that the grid itself (an empty period clamped to where
the data stops, see the next entry) already carries the retention story visually, and a permanent paragraph
under a hover surface was reading as clutter rather than as a needed disclosure. The payload fields are
unchanged and still drive the day/week window clamp below — only the standing prose describing `source`/
`scope` was cut. Three options were rejected for the underlying multi-table split: scoping *every* mode to one
champion (throws away the "how much did you play" answer the grid is for), splitting the aggregate by day
(it has no day), and quietly labelling all four "activity" (that is the silent disagreement the metadata
exists to prevent). Two consequences kept on purpose: the patch total is a *different population* from the
day total over the same patch, and all four granularities ship in **one** request — three of them are
foldings of the same rows, so one snapshot is both cheaper and the only way two modes cannot describe two
different afternoons — #927.

**An erased period is not an idle one: the calendar grids stop where the data stops.**
The day / week series clamp their window to the oldest game still on disk and emit **no cell** before it,
rather than drawing 30 empty squares over a month retention deleted. Inside the covered range an empty cell
*is* emitted, with `games: 0` and a **null** win rate — a 0% period is a measurement and an idle one is not,
so the fill helper returns no colour at all for the second and the tooltip reads "No games". A player whose
whole retained window has been pruned gets three empty series and a populated patch series, with copy pointing
at it. Days and weeks are UTC (Monday 00:00 for weeks), matching the pipeline's existing UTC-day rule (#907):
a viewer-local grid would need a timezone parameter on a public read or client-side re-bucketing of raw games.
Accepted: a late-night game can land on the next day's cell far from UTC — #927.

**Patch mode is wired to the dedication card's own numbers, not to a parallel query.**
It resolves the signature champion through `MainDedication` — the single place that decides what one is — and
groups the exact scope rows that helper's career lateral sums (account + champion + ranked queue, no platform /
position / bracket narrowing). That makes `patch.games == dedication.careerGames` and
`patch.buckets.length == dedication.patchSpan` invariants a reader can check by eye, since the two panels sit
centimetres apart. A narrower filter (per platform, per lane) would have been defensible on its own and would
have made the grid disagree with the card above it — #927.

**The matchup opponent-search stays a live query while the matchups panel reads the aggregate.**
The `opponent=` path filters to a single adversary (already fast) and uses a floor of 1 game, which an
aggregate built at floor 10 cannot serve — #606.

**The draft tool is the "Matchup" page (`/matchup`), and its opponent is the *role* opponent.**
"Lane opponent" is meaningless for a jungler, and the page is not a build editor — it answers "what do I build
into this opponent". The wording is now `role opponent` everywhere in that feature, down to
`CompositionSearchOptions.RoleOpponentWeight`. `/builder` stays alive as a query-preserving redirect (the three
matchup inputs are deep-linked, so shared links exist) and is excluded from the sitemap. The *laning-phase*
vocabulary elsewhere — performance score, power spikes, champion matchups — genuinely means the lane and was
left alone — #939.

**The recommendation shows no situational-items row.**
#921 rendered the API's `situationalItems` (off-core items with pick/win rates) under the build panel. It
restated what the build tree above it already shows, minus the ordering, so #939 removed the whole chain —
component, read-model field, backend aggregation and the `SituationalItemCount` option. Reviving it means
reviving the aggregator, not just the component — #939.

**The static champion list drops Data Dragon entries with an id at or above 10 000 — alternate-mode kits, not champions.**
Patch 16.15 ("League classique") added 60 legacy kits to `champion.json`: alias `Jade_<BaseAlias>`, key
`60000 + <base key>`, and the *same display name* as the original — `Jade_Ahri` (60103) sits next to Ahri
(103). The ingestor only aggregates queue 420, so those ids never carry a single stat: unfiltered they
doubled every search hit and picker row with a dead end and put 60 empty pages in the sitemap. The floor is
`isLiveChampionId` in each app's `shared/utils/ddragon.ts`, applied in the one endpoint both apps read
(`server/api/static/champions.get.ts`). 10 000 rather than 60 000: the highest real Riot key is 950, so the
cut keeps an order of magnitude of headroom while catching a future mode built the same way — #966.

---

## Data & storage

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

**Lane win rate stores three counters and divides by the *decided* lanes, not by games played.**
A gold *threshold* at 15 min (`LaneOutcomeAggregation:GoldLeadThreshold`, 300) is what defines a won lane, and
a threshold necessarily creates a third outcome: lanes inside the band were neither won nor lost. Folding those
into losses would print "lane lost" where nothing was decided, so wins and losses are stored separately and the
rate divides by their sum. `LaneGames` is separate from the matchup's `Games` for a different reason: a match
with no ingested timeline, or one that ended before 15 minutes, is a game but not a judgeable lane — dividing
by `Games` would understate every lane win rate by the share of those. The threshold is configurable because it
is a product judgement, but changing it re-defines every stored counter and frozen patches can never be
recomputed (#466), so old rows keep the threshold in force when they were folded.
⚠️ #919's own premise was stale: it assumed the per-matchup 15-min leads were already aggregated from #606/#595,
but #889 dropped that aggregate with the "Lead vs role opponent" chart. What survives is the raw timeline
snapshot at the canonical marks, which is why the fold flag could ship `false` and pick up the whole retained
window instead of starting at deploy like #920's bans — #919.

**Matchups are a pre-aggregated read table — explicitly revising the earlier live-self-join design.**
#90 chose a self-join "for simplicity, not volume". Prod measurement showed the aggregate is bounded by
dimensions rather than games: ~22.2k rows, a few MB, versus a self-join over ~35 GB running single-threaded.
Reads became ~13-row indexed selects — #606.

**Daily storage snapshots go to Mongo and are keyed on the day, not the run.**
Storage history is append-only, time-ordered, ops-only telemetry with no relational joins — the exact
criteria that put logs and metrics in Mongo below — and a native TTL index prunes it for free instead of
needing its own retention arm in `MatchDataRetentionProcess`. Keying the document on `(day, table)` rather
than on the capture instant is what makes the writer safe to run every pipeline pass: prod runs the ingestor
`RunOnce` + `restart: unless-stopped`, so it re-runs back-to-back many times a day, and a day-keyed upsert
turns that into "refresh today's reading" instead of "append another point". No scheduler, no "have I run
today" guard, and a container restart can neither lose nor duplicate a day — #925.

**The disk forecast is absent rather than approximate when the data can't support it.**
It declines to project on fewer than 3 days of history, on flat or shrinking storage, on a crossing more than
a century out, and when no disk capacity is configured; the panel prints which of those applies. A forecast is
the one number on the admin that is invented rather than measured, and an operator who cannot tell a fitted
date from a placeholder will either ignore all of them or act on a fabricated one. Related: the projected
figure is measured `pg_database_size`, not the sum of `pg_total_relation_size` over public tables — the sum
excludes catalogs and would under-report what actually fills the volume that #680 filled — #925.

**Logs and metrics live in MongoDB, not Postgres, with two different guarantees.**
Mongo has native TTL retention and suits append-heavy time-ordered payloads. `logs` is lossy (bounded channel,
batched, drop-on-overflow — fine for diagnostics); `audit_events` is lossless and synchronous. Existing
Postgres log rows were deliberately not migrated — #416.

**Ops logs are signal-only: Polly retry noise is not persisted, domain events are.**
Every Riot 429 emitted `Execution attempt` + `OnRetry` warning pairs — dozens per minute while rate-limited —
burying everything useful — #444.

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

**Postgres columns stay quoted PascalCase; only tables are snake_case.**
#227 proposed `UseSnakeCaseNamingConvention` for columns and was **closed as won't-do on 2026-07-28**: a
full column rename is a large, risky migration for a cosmetic gain, and raw SQL, prod psql habits and the
compiled model all rely on the current naming.

---

## Performance & incidents

**A starter-item scan must only trust events that prove ownership, not every event mentioning an item**
(`Data/BuildFacts/StarterItemAnalyzer.cs`, found via #923's matchup filter). The support-quest fallback
scans a participant's full event stream for a completed quest item (Celestial Opposition, Dream Maker,
Solstice Sleigh, Bloodsong) to surface it in the starter slot — necessary because World Atlas is
auto-gifted with no `ITEM_PURCHASED` event. But junglers' timelines carry six to eight `ITEM_DESTROYED`
events *naming* those same completions (an artifact of Riot's event stream, not anything the jungler
owned), and the scan originally treated any reference as proof. Result: a jungler's starter basket read
"Scorchclaw Pup + Bloodsong + Health Potion" — 400 g injected past the 500 g budget. Fixed by only
counting `ITEM_PURCHASED` / `ITEM_UNDO` as ownership for a *completion*; a destroyed root or intermediate
still counts (that is exactly what a support's gifted World Atlas looks like transforming). The general
rule: an event-stream heuristic that infers state from "this item was mentioned" rather than "this item
was proven owned" will eventually be poisoned by a class of player the heuristic wasn't built for.

**A matchup-scoped build page is folded live, not aggregated** (#923). `champion_matchup_stats` carries the
opponent dimension but only games/wins; the pattern aggregates carry the build data but are grained on
(account, champion, patch, platform, queue, position, elo) with no opponent — so a matchup build has no
aggregate to read, and adding an opponent dimension would multiply the scope rows by the roster. Measured on
production before deciding (patch 16.13, per champion x opponent x position, all elos): 49 411 pairs, median
**4 games**, p75 18, p90 79, p99 355, max 1 562, with only 24% of pairs reaching 20 games. Two consequences:
the live fold is cheap (a few hundred rows, capped at 2 000 for the tail), and **the binding constraint is the
sample, not the latency** — which is the opposite of what the issue assumed. Decided 2026-07-30: show the
matchup whatever its volume, with the game count on every variation, rather than hiding thin sections or
silently widening the window. The reader is told how thin the answer is instead of being given a fabricated one.

**An expensive read path behind a TTL cache needs a single-flight, not a lock** (#870,
`Api/Services/RequestCoalescer.cs`). The cache protects steady state; the stampede happens on the first
request and at every TTL expiry, when concurrent callers all miss at once and each start the same scan — up to
50 000 scored accounts for one dedication ranking. Concurrent callers now share the in-flight `Task`. A per-key
lock was rejected: it serialises callers *and* still runs the work N times whenever the result is too large to
cache, turning a burst into a queue of full scans. The shared pass is detached from any one request's token —
a caller who walks away must not cancel the pass the others are waiting on — so only bounded work belongs in it.

**Postgres runs with `max_parallel_workers_per_gather=0` in every compose file — do not re-enable.**
2026-06-25 prod crash-loop: `53100: could not resize shared memory segment` — per-worker DSM segments
exhausting the container's 256 MB `/dev/shm` in bursts. EF read it as transient and retried, which under
`restart: unless-stopped` became an API + ingestor crash loop. Disabling beat raising `shm_size` because it is
deterministic (raising only moves the ceiling) and the API workload is index/PK reads — #589.

**Consequence: every heavy aggregate runs single-threaded, so batch work must be chunked.**
Three separate 300 s command-timeout incidents followed — pattern aggregation's `DISTINCT ChampionId` scan
(#603), timeline leads (#594), and harvest candidate aggregation (#632, dead since 2026-07-03). The last one
is instructive: *no code changed*, it was a slow threshold crossing.

**Aggregation is chunked per champion to bound memory.**
The ingestor was OOM-killed at ~6 GB managed heap because the source reader materialised every live-patch
participant *with* its item/skill event JSON and the builder held a second copy. An OOM bypasses the
per-process try/catch, and `restart: unless-stopped` replayed the same step — a crash loop that took the whole
VPS down — #600.

**A heavy `CREATE INDEX CONCURRENTLY` must never be a startup migration.**
The #595 covering index exceeded the migrator's `Command Timeout=300` (CONCURRENTLY also waits for the
ingestor's in-flight transactions to drain) → `TimeoutException` → API crash-loop retried on every restart →
deploy aborted on an unhealthy container, leaving an INVALID index that needed its own cleanup migration —
#597 / PR #598.
**Refinement, not a reversal:** once `match_participants` shrank to ~2M rows / ~11 GB after #680, the index was
rebuilt as a *plain transactional* migration — the timeout risk no longer held at that size, and
`CONCURRENTLY` plus a manual pre-create step meant prod's schema was not reproducible from migrations alone.
Accepted: a `SHARE` lock stalls the ingestor for the tens of seconds of the build — PR #750.
👉 The rule is about **heavy** index builds, not about partial single-column indexes on a table of this size.

**Npgsql pools are capped per service (api 50, ingestor 20) against Postgres `max_connections=100`.**
Two unbounded pools defaulting to 100 each could request 200 connections; once truemain.lol went live this
produced `53300: sorry, too many clients already` and a total API outage. PgBouncer was proposed as the proper
fix; the caps are what actually shipped (no `pgbouncer` service exists in the compose files) — #437, #461, #462.

**The ingestor Worker isolates failures per process.**
Discovery's 40 s total timeout (a regression from deriving the total from attempt count, which Riot's
`Retry-After` easily exceeds) crashed the first process in a plain `foreach`, so **nothing else in the pipeline
ran between May 30 and June 12** — #443.

**The EF compiled model must be regenerated on every schema change.**
`dotnet ef dbcontext optimize` → `Data/CompiledModels`. Originally for cold start; the operational reason is
that a stale model *silently drops columns*. Two concurrent schema PRs always conflict there — the second
merged must re-merge develop and regenerate — #242, `CLAUDE.md`.

---

## Infrastructure & deploy

**The admin portal is a standalone Nuxt app with its own deployment and domain — not a `/admin` route.**
Decided 2026-06-09. Auth is username/password + a signed httpOnly session; the app injects `X-Ops-Key`
server-side so the ops key never reaches the browser. Native composables, no Pinia — #96, #91, #376.

**Preprod auto-deploys from `compose.preprod.yaml` on every push to `develop`; prod auto-deploys from `compose.prod.yaml` only when a GitHub Release is published.**
Merging to develop or master alone never reaches prod. Both use the `hostinger/deploy-on-vps` action (a pure
API call, no SSH material in CI). Prod and preprod are on **separate Hostinger accounts**, so each needs its
own account-scoped token — `docs/prod.md`, `docs/preprod.md`, #717, #751.

**Images publish an immutable `:<sha>`/`:<version>` tag alongside the moving `:preprod`/`:latest`, and compose references the immutable one.**
The classic mutable-tag trap: the deploy job went green while the VPS kept running days-old containers,
because handing Docker Manager an unchanged compose spec never triggers a pull or recreate. Changing the
referenced image name every merge forces the pull. Doing so then surfaced a hidden GHCR `unauthorized`
failure the mutable tag had been masking — #738, #765, #767.

**Prod deploys from the version-controlled compose file — no hand-maintained host compose.**
A divergent host-only `docker-compose.yml` meant the pool-cap fix never reached prod, the uncapped pools kept
running and the `53300` outage returned — #462.

**Prod still applies EF migrations on startup; moving to an out-of-band script is an OPEN decision.**
`ApplyMigrationsOnStartup: "true"` in `compose.prod.yaml`. Microsoft advises against it (concurrency, elevated
privileges, no review or rollback), and `docs/production-migrations.md` documents the script path while
stating that flipping the flag is deliberately left to the owner. This is why the "no heavy startup migration"
rule above is load-bearing — #208, #246.

**Preprod tracks `develop`, has its own Riot API key, and is deliberately tiny — a new key forces an empty database.**
PUUIDs are encrypted per API app, so key and database are an inseparable pair: old data is unusable with a new
key. Preprod runs every pipeline stage at reduced volume with 1-patch retention — `docs/preprod.md`, #705.

**OG image rendering is on, pinned to Satori + resvg, and deliberately reaches exactly two pages.**
It shipped disabled in #551 for one stated reason — "no dedicated share artwork yet, so the toolchain
would be build weight for no benefit". #926 supplies the artwork, which flips the benefit half but not
the cost half, so the setup stays narrow: the `.satori.vue` suffix pins the renderer (no `.browser.vue`
component exists, so playwright and a headless Chromium never enter the image), only `/champions/:id`
and `/truemains/:nameTag` call `defineOgImageComponent()`, and every render is cached for 1 h. That
matters because the renderer runs inside the web container on a VPS that has already been taken down by
one process's memory (#600) — the crawler-only traffic pattern is what keeps it cold. The **takumi**
renderer was rejected as still beta; a hand-rolled SVG→PNG route through the already-present `sharp` was
rejected because `node:*-alpine` ships no system fonts, so text would not render — Satori takes font
buffers directly, which is exactly the problem it solves — #926.

**OG image URLs are signed with a secret regenerated at every build, and that is left as the default.**
Without a secret, the encoded URL params are attacker-controllable, including the module's `html`
option — an arbitrary-HTML renderer on our own origin. Pinning a stable `NUXT_OG_IMAGE_SECRET` would
have to happen at *build* time (the module reads it in `setup()`), i.e. a Docker build arg plus a CI
secret. Accepted consequence instead: URLs minted by a previous build return 403 after a redeploy, which
only bites a re-unfurl of an old message — Discord and X have long since cached the bytes, and any fresh
unfurl re-reads the page and gets the current URL. Revisit if broken previews on old links are ever
reported — #926.

**Caddy terminates TLS and is the only public entry point in prod.**
Admin login was impossible over cleartext HTTP because the session cookie is sealed `Secure` in production
builds. Caddy also normalises `X-Forwarded-For` (dropping client-supplied values), which is what makes the
admin brute-force throttle non-spoofable. DNS must be **DNS-only, not Cloudflare-proxied**, or the ACME
challenge fails — #433, #430, #426.

**`/ops/*` is the only authenticated API surface** (`X-Ops-Key`, min 32 chars, rotated independently of the
Riot key). Everything else is public and rate-limited to 100 req/min per IP — `docs/api.md`.

**The Riot API key is a permanent *personal* key — not a 24 h dev key, and not production-approved.**
(Owner-confirmed terminology, 2026-07-28: do not call it a dev key.) The production application is submitted
and pending review. It declares ACCOUNT-V1, SUMMONER-V4, MATCH-V5, LEAGUE-V4 and CHAMPION-MASTERY-V4 —
**not SPECTATOR-V5**. Consequences: no live-game features (#532, parked P3), no RSO and therefore no user
accounts (#780), ingestion is rate-limited, and data changes are forward-only because backfill is not
possible. Approval is the single external unlock for all of it — #780.

---

## Conventions

- **Language split**: talk to the user in French; everything committed or published (code, comments, commits,
  issues, PR titles and bodies) is in English. (`docs/api.md` is in French, predating the rule.)
- **`develop` is the default branch.** All PRs target it; the only PR allowed to target `master` is the release
  PR. Feature PRs are **squash**-merged; release PRs use a **merge commit**, because squashing there creates
  false conflicts on the next release. `develop` survives release merges and is never deleted.
- **Branch names** `<type>/<issue>-<short-kebab>`, conventional commits, no `Co-Authored-By: Claude` trailers,
  no "Generated with Claude Code" footers, `Closes #N` in the PR body.
- **A PR is done** when CI is green, the Claude review verdict is clean *on the current head SHA* (verdicts
  trail ~1 commit behind pushes), and every real finding is fixed or rebutted — then merge without asking.
  Stop after ~3 non-converging iterations and report blockers instead of looping.
- **CI traps**: backend CI builds **Release** with analyzers as errors (Debug is not enough); `nuxt typecheck`
  can pass on stale `.nuxt` types while CI's `nuxt build` fails; `web/package-lock.json` must be regenerated
  with `npx npm@11.13.0` (older npm omits sharp optional deps).
- **API wire conventions**: camelCase JSON, RFC 7807 problem details on all 4xx/5xx, no global `/api` prefix,
  `patch` normalised to `major.minor` (invalid values treated as unfiltered), canonical Riot position values,
  `pageSize`/`limit` ≤ 0 means "default" — `docs/api.md`.
- **Every issue goes on GitHub Project #2.** Priority is the sprint bucket: P0 current, P1 next, P2 after,
  P3 someday. No milestones.
- **A health panel may not pass what it did not measure** (#924). Detector verdicts are green / amber / red /
  **unknown**, and `unknown` never means "fine": an unmeasurable row outranks green in the roll-up (one
  unchecked platform must not let a card claim to be clean) but stays below red (it must not hide a real
  failure either). Headlines are worded from the verdict, not from the count, so a card can never read
  "everything completed" while its colour says "not measured". The mirror-image rule matters just as much: a
  signal that is *deliberately* unavailable — a starter basket's canonical order (patch-dependent prices), a
  trend with no previous window, a patch too new or too old to compare — is shown as a row but **does not
  vote**, because a card pinned to unknown for ever teaches the operator to ignore its colour.
- **A detector shares the repair's definition of the bug it audits** (#924). The canonical-key SQL lives once,
  in `Data/DataQuality/ChampionDimensionCanonicalKeys.cs`, and is read by both the ingestor's
  `RunePageDeduplicationProcess` and the admin duplicate detector. Two copies would eventually disagree, and a
  detector that groups differently from the repair reports a clean bill of health for a live bug (#911).
- **A row rendered on more than one surface sizes off its own width, not the viewport** (#967).
  `MatchRow` and `LeaderboardRow` are `@container`s. The same row sits full-width on a page, in a ~33rem
  drawer and in a sidebar, so a viewport `xl:` breakpoint told the narrow copy it owned the page and its
  fixed columns spilled into its own `overflow-hidden` clip — invisible on the surface it was tuned for,
  broken everywhere else. Content degrades by tier as the row narrows (compositions, then secondary stats,
  then the loadout wrapping onto a second line) rather than being cut off. `pages/dev/match-row.vue` renders
  the row at each tier width so the compact layouts are reviewable without reproducing the host surface.
- **Detector thresholds are configuration, not constants** (`DataQualityDetectors:*`). The honest line differs
  between preprod and production, and an operator silencing a crying-wolf card must not need a redeploy. A
  level of `0` disables it, which is how a warning-only signal is expressed.

---

## Where API reads live

**Decided 2026-07-30 (#865): the documentation was stale, and now matches the code.** API reads are
purpose-built **query services** returning read-models, living in `Api/Services/<area>` beside the endpoints
they serve, injecting `TrueMainDbContext` and projecting with `AsNoTracking`.

The rule had said reads live in `Data` with `Api` staying persistence-ignorant. Measured on 2026-07-30, 83
`*QueryService*.cs` files sat under `backend/Api/Services`, 36 of them injecting the context, while
`backend/Data` held only Mongo-side query objects (`MongoLogQuery`, `CrashQuery`, `RiotApiUsageQuery`). The
contradiction was re-litigated on six separate PRs, each time resolved by following the code and leaving the
doc alone — which is the right short-term reflex and the wrong durable answer.

What was kept, because it held everywhere and is the half that matters: **no generic `IRepository<T>` for
reads.** Every read path is shaped by the question it answers, returning a read-model rather than entities.
The rejected alternative was migrating 36 services (plus tests and DI) into `Data` for an architectural
benefit no user would ever see.

`Data` still owns: the schema (entities, configurations, migrations, the compiled model), the Mongo-side query
objects, and **SQL that must not diverge between two consumers** — e.g.
`Data/DataQuality/ChampionDimensionCanonicalKeys.cs`, read by both the ingestor's rune-page repair and the
admin duplicate detector (#924). That last one is the durable reason `Data` holds any query text at all: a
detector that groups differently from the repair it audits reports a clean bill of health for a live bug.

Counts drift with every PR — re-measure rather than trusting the numbers above:

```bash
find backend/Api/Services -name "*QueryService*.cs" | wc -l
grep -l "TrueMainDbContext" $(find backend/Api/Services -name "*QueryService*.cs") | wc -l
```

---

## Keeping these files current

A PR that ships a user-facing feature, removes one, or reverses a decision here **must update
`features.md` / `decisions.md` in the same PR**. These files are the context a fresh session loads instead of
re-reading the codebase; stale entries are worse than missing ones.
