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

**Matchups are a pre-aggregated read table — explicitly revising the earlier live-self-join design.**
#90 chose a self-join "for simplicity, not volume". Prod measurement showed the aggregate is bounded by
dimensions rather than games: ~22.2k rows, a few MB, versus a self-join over ~35 GB running single-threaded.
Reads became ~13-row indexed selects — #606.

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

---

## Known discrepancy

**Where API reads live.** `CLAUDE.md` and `README.md` state that reads live in `Data` as purpose-built query
objects, with `Api` staying persistence-ignorant. In practice most Postgres reads live in `backend/Api/Services`
as query services injecting `TrueMainDbContext` directly, and `backend/Data` holds only Mongo-side query files
(`MongoLogQuery`, `CrashQuery`, `RiotApiUsageQuery` and their interfaces). The *second half* of the rule does
hold everywhere — reads are purpose-built query objects returning read models, and there is no generic
`IRepository<T>` for reads. Only the **location** differs.

Measured on 2026-07-28: 74 `*QueryService*.cs` files under `backend/Api/Services`, 32 of them injecting
`TrueMainDbContext`. These counts drift with every PR — re-measure rather than trusting them:

```bash
find backend/Api/Services -name "*QueryService*.cs" | wc -l
grep -l "TrueMainDbContext" $(find backend/Api/Services -name "*QueryService*.cs") | wc -l
```

**#865 tracks the choice** between "the doc is aspirational, label the divergent services as known debt" and "the doc is
stale, restate the rule as practised". Do not quietly rewrite either side — that would erase a possibly
intended migration.

---

## Keeping these files current

A PR that ships a user-facing feature, removes one, or reverses a decision here **must update
`features.md` / `decisions.md` in the same PR**. These files are the context a fresh session loads instead of
re-reading the codebase; stale entries are worse than missing ones.
