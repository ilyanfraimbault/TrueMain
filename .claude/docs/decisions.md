# TrueMain — decision log

Settled product and architecture decisions **with their rationale**, so a session doesn't re-litigate them or
propose something a past incident already ruled out. Companion file: [features.md](features.md) (the *what*).

This file is the **index**: one line per decision, grouped by the file that holds its full entry. Read the
index to know whether a decision exists, then open only the file you need — each holds the verbatim entries
(**Decision** — why — `source`) for its area. Do not paste a decision's body back here.

Last verified against `develop` on 2026-09-02.

## Mains, dedication and candidate intake — [`decisions/product-mains.md`](decisions/product-mains.md)

- Ranked solo/duo (queue 420) is the only queue stored; match history is solo/duo-only by design — #680
- Dedication score (0–100) is the signature metric, always scoped to one champion — #530
- The `/truemains` leaderboard is strictly `IsMain=true` — #184
- Leaderboard games/KDA/WR come from frozen aggregate scopes, not live `match_participants` — #719
- Inactive mains are retired via champion-mastery `lastPlayTime`; intake favours depth over breadth — #900
- Candidate scoring is scarcity-weighted and the `IsMain` threshold is coverage-adaptive (0.20 → 0.12) — #407
- Thin samples degrade, they don't 404 — #762
- The account explorer (#1032) reports raw score inputs, not score components — and never calls Riot
- `MainActivity` deactivation carries no persisted reason, so the account explorer says so rather than guessing — #900
- A main whose matches expired is dated, not deleted and not hidden (2026-08-24) — #825, #466

## Population and rank scope of the champion pages (#1346) — [`decisions/product-population.md`](decisions/product-population.md)

- Stats are computed from *true mains* by default, and that default is now a filter rather than the pipeline's only setting — #1346
- The champion pages open on Master+, not on every rank — #1346
- The truemains population is one boolean on the scope row, not a duplicated aggregate — #1346
- The patch the site serves is resolved over mains only, never over the selected population — #1349, #1346, #1109
- Widening the aggregate meant auditing every existing read, because the dangerous direction is silent
- The widened population is gated on a flag, because "a separate ops step" was not true otherwise — #1346, #1349, #601
- A demoted account's scopes are demoted, not deleted — once the widening is on — #1346
- Matchups reject the widened population rather than ignoring it — #1346, #1087
- The thin-sample caveat is a header tooltip, and it counts games rather than coverage — #1346

## Champion page — builds, power spikes, SSR prose — [`decisions/product-champion-page.md`](decisions/product-champion-page.md)

- Champion timeline-leads ("Lead vs role opponent") was removed; matchups stayed — #889
- Power spikes are per-core-build bars anchored on events, not a time curve; bar height is excess acceleration — #890, #775
- Scoping the aggregate to a build scopes the games, not the items — the item set has to be intersected at read time — #1021, #1022
- "Completed item" is the build path's eligibility rule, shared, not a local restatement of it — #1021
- Matchup-scoped power spikes are a dimension on the aggregate, not a live recompute — because a live recompute is impossible — #923, #957, #772
- Champion URLs are bare slugs, and the slug map is app state rather than per-page async data — #1124
- The champion page server-renders its build as prose — the one SSR round-trip the page pays — #926, #1123, #1273
- The build paragraph's cache window is 5 minutes, because it sits next to live numbers — #1123, #926, #1273
- `hydrate-on-visible` does not make a panel free — it defers hydration, not server rendering — #1123, #1231
- The build paragraph is typeset, and rune trees get Riot's colours to do it — #1123, #1143
- The paragraph's hover cards are resolved client-side, not carried in its payload — #1147, #1145
- Roaming is a badge in the header, not a panel — #536

## Champion directory, tier list and served patch — [`decisions/product-directory-and-tiers.md`](decisions/product-directory-and-tiers.md)

- Tier score is presence-first (pick rate + ban rate, weighted above a sample-shrunk win rate) — this reverses #920's "bans don't feed the tier score"
- The static champion list drops Data Dragon entries with an id at or above 10 000 — alternate-mode kits, not champions — #966
- A champion gets at most two lines in the directory — its dominant lanes — and the cap is applied before tiering — #1082
- A tier-list chip is a portrait and its lane badge — the name and the three rates are tooltip content
- A patch is served only once it can fill a directory (2026-08-12) — #1109, #1107
- The homepage hero counts a lifetime, compactly, and never names a patch (2026-08-16) — #1109

## Champion synergies — [`decisions/product-synergies.md`](decisions/product-synergies.md)

- Champion synergy is observed *minus expected* win rate, never the raw pair win rate — #922
- The expected value needs two different baselines plus a cohort intercept — one baseline would bias every number — #922
- Trios are computed on demand from a chosen pair; the triple space is never pre-aggregated — #922
- Synergies floor on a share of the champion's games *and* on whether the partner plays that lane at all — #1087, #1090
- Synergy carries three separate sample floors, and a thin sample yields no entry rather than a hedged one — #922

## Matchups and the draft tool — [`decisions/product-matchups.md`](decisions/product-matchups.md)

- Matchups are a pre-aggregated read table — explicitly revising the earlier live-self-join design — #90, #606
- The matchup opponent-search reads the aggregate, like the panel — this reverses #606's "the search stays live"
- The matchups leaderboard floors on a *share* of the champion's games, and ranks on Wilson bounds, not the raw rate — #1087
- The lane win rate carries its own floor, because it is its own sample — #1087
- The matchup folds count mains of the champion, not every account we know — #1087
- Every champion-page fold takes its cohort from one place, and a remake is not a game — #1365, #1087, #922
- The matchups panel follows the page's patch filter on the global route, and deliberately does not on the player one — #1087
- Lane win rate stores three counters and divides by the *decided* lanes, not by games played — #466, #919, #606
- A matchup-scoped build page is folded live, not aggregated — #923, #1075, #1098
- The draft tool is the "Matchup" page (`/matchup`), and its opponent is the *role* opponent — #939
- The recommendation shows no situational-items row — #921, #939
- The matchup tool judges the lane over its own sampled games — this finishes #1111's merge
- `/matchup` carries one line of numbers, not two — and it stores the XP gap beside the gold one — #1098, #976, #1087

## Player profile — [`decisions/product-player-profile.md`](decisions/product-player-profile.md)

- The activity grid's four modes read two different tables, and the response says so rather than reconciling them — #466, #959, #927
- An erased period is not an idle one: the calendar grids stop where the data stops — #907, #927
- Patch mode is wired to the dedication card's own numbers, not to a parallel query — #927
- The player performance panel shows the score and its sample only — no per-component breakdown — #918
- The rank chart is one area with a tier-gradient line, never one area per tier (2026-08-20)
- A Riot ID resolves case-insensitively, in exactly one place (2026-08-26)

## SEO, share cards and OG images — [`decisions/product-seo-and-sharing.md`](decisions/product-seo-and-sharing.md)

- Share cards degrade in three steps and never print a number the API did not return — #920, #926
- Share cards resolve their own data server-side instead of receiving it from the page — #149, #926
- The champion link graph was server-rendered, then removed — the pages are back to zero internal champion links — #1123, #1209, #147
- A platform-dependent `UKbd` cannot be server-rendered — #1209
- OG image rendering is on, pinned to Satori + resvg, and deliberately reaches exactly two pages — #551, #926, #600
- OG image URLs are signed with a secret regenerated at every build, and that is left as the default — #926
- The sitemap advertises champions, not players (2026-09-01) — #862, #1337, #551

## Web frontend rules (hydration, fetches, icons, tooltips) — [`decisions/web-frontend-rules.md`](decisions/web-frontend-rules.md)

- "Client-only fetch" has to be enforced on the *side*, not merely intended — an immediate watcher is not client-only — #862, #1234
- A closed `enabled` gate resolves `success` with an empty model, so the gated composables expose their own `pending` — #1234
- Every hand-rolled fetch composable carries a monotonic request token — #1234
- A row rendered on more than one surface sizes off its own width, not the viewport — #967
- A tooltip trigger keeps the same DOM element for the life of the component
- A skeleton is the real component in `pending` mode, not a drawing of it
- Icon slots are rendered from the ids, never gated on a resolved static lookup
- Champion-page icons are slow because of browser queue depth, not the image proxy — measure the split before "optimising" it — #680, #997
- `SkeletonImage` serves WebP; `RankIcon` deliberately does not
- Every icon URL is built by one helper, so one asset is one cache entry — #1000
- The `/_ipx/**` cache evicts by patch, keeping the current patch and the two before it — #997
- `web/` and `admin/` duplicate their Data Dragon helpers on purpose, and the copies are labelled (2026-08-26) — #1226, #947, #966

## Design system — [`decisions/design-system.md`](decisions/design-system.md)

- A failed icon is hollow; a loading one is solid and moving (2026-09-02) — #1396

- The rose-gold-only surface rule is reversed: neutral surfaces, a scarce accent, and a data axis of its own (2026-08-10) — #1060, #1059, #927
- Measurements are rose gold again: the cold→warm data axis is withdrawn (2026-08-11) — #1096, #1060, #927
- Measurements are set in Inter again: the mono stat face is withdrawn — #1060, #1111

## Aggregates, retention and the schema — [`decisions/data-aggregation.md`](decisions/data-aggregation.md)

- Champion aggregates are replace-by-scope on *live* patches only. Old patches are frozen and must never be wiped — #466, #606, #694
- Aggregate retention is opt-in per environment: `AggregateRetainedPatchCount` defaults to 0 (frozen forever); preprod sets 2 — #711
- Timeline snapshots are pruned to the canonical marks {5, 10, 15, 20, 30} once a match is powerspike-aggregated — #772, #694
- Aggregation is incremental per match, flagged on `matches`, never a full recompute — #811, #922, #920
- Ban rate is its own aggregate pair with a stored denominator, and `ALL` is a stored band — not a summed one — #920
- No pick+ban "presence" figure, despite it being standard elsewhere — #920
- Dimension rows must be stored in a canonical order, not the order Riot reported them — #911
- Rank snapshots are capped at one row per account per UTC day (DB-level unique index) — #907
- `(GameName, TagLine, PlatformId)` on `riot_accounts` is a plain, NON-unique index. PUUID is the only real identity — #901, #902
- PUUID indexing is intentional — do not propose dropping it or migrating to `RiotAccountId`-only — #123, #124
- Pattern aggregates use a junction model (`champion_aggregate_patterns` + globally deduplicated `champion_dim_*`)
- The patch is a column on `matches`, not a `LIKE` prefix over `GameVersion` (2026-09-02) — #1368, #589, #598

## Backend code conventions — [`decisions/backend-conventions.md`](decisions/backend-conventions.md)

- Where API reads live — #865, #924
- Tables are snake_case, columns are PascalCase — and enums split by who reads them (2026-08-28) — #1251
- Postgres columns stay quoted PascalCase; only tables are snake_case — #227
- The EF compiled model must be regenerated on every schema change — #242
- Configuration defaults live in the class, and the two champion games floors are two keys — #1034, #860, #889
- A unit of work covers the writes and nothing else (2026-08-28) — #264, #1229

## Ingestion pipeline — Riot budget, pacing and intake sizing — [`decisions/pipeline-riot-budget.md`](decisions/pipeline-riot-budget.md)

- Riot calls are paced by a limiter keyed on the routing value, because that is the grain Riot enforces — #1359, #855
- The pipeline runs as two lanes — Riot-bound and Postgres-bound — because they have opposite bottlenecks — #1362, #1360
- The lane is derived from the process name; the mode it ran under is recorded (2026-09-02) — #1362
- Match ingestion fans out one worker per platform, and stays sequential inside one — #1359
- A Riot call that stores nothing is a bug, not a cost (2026-09-02) — #1358, #1357, #1312
- The intake is sized by the claim, not by the ladder (2026-09-02) — #495, #900, #1150
- Region balance is a target, not a quota: coverage deficit allocates every budget (2026-08-19) — #1149, #495, #900

## Ingestion pipeline — processes, leases and resilience — [`decisions/pipeline-ingestion.md`](decisions/pipeline-ingestion.md)

- A starter-item scan must only trust events that prove ownership, not every event mentioning an item — #923
- The ingestor Worker isolates failures per process — #443
- A streamed Riot response can fail *after* the resilience handler has waved it through — isolate the call site — #253, #1052
- A CommunityDragon patch branch that does not exist yet is a transient condition, not a fatal one — #1107
- Ranks are read from the ladder, not from one account at a time (2026-08-30) — #788, #1312, #1149
- A lease is only kept if something reaps it (2026-09-01) — #1344
- Jungle first-clear tracking was built, then removed entirely (2026-08-24) — #1186, #1195, #535

## Performance, caching and incidents — [`decisions/performance-and-incidents.md`](decisions/performance-and-incidents.md)

- An expensive read path behind a TTL cache needs a single-flight, not a lock — #870
- Postgres runs with `max_parallel_workers_per_gather=0` in every compose file — do not re-enable — #589
- Consequence: every heavy aggregate runs single-threaded, so batch work must be chunked — #603, #594, #632
- Aggregation is chunked per champion to bound memory — #600
- A heavy `CREATE INDEX CONCURRENTLY` must never be a startup migration — #595, #597, #598
- Npgsql pools are capped per service (api 50, ingestor 20) against Postgres `max_connections=100` — #437, #461, #462
- Postgres ships tuned settings in compose, and parallelism stays off (2026-09-02) — #1366, #589
- Champion reads are cached until the data changes, not for 60 seconds (2026-09-02) — #1374, #1368

## Infrastructure and deploy — [`decisions/infrastructure-and-deploy.md`](decisions/infrastructure-and-deploy.md)

- The admin portal is a standalone Nuxt app with its own deployment and domain — not a `/admin` route — #96, #91, #376
- Preprod auto-deploys from `compose.preprod.yaml` on every push to `develop`; prod auto-deploys from `compose.prod.yaml` only when a GitHub Release is published — #717, #751
- Images publish an immutable `:<sha>`/`:<version>` tag alongside the moving `:preprod`/`:latest`, and compose references the immutable one — #738, #765, #767
- Prod deploys from the version-controlled compose file — no hand-maintained host compose — #462
- Preprod and prod both apply migrations out-of-band, as a discrete CI step before the images roll — not at startup — #208, #246, #1058
- An incomplete prod deployment configuration fails the release run; it is never a green skip — #1228
- A deploy job proves the environment moved; the API acknowledgement is not evidence — #1394, #1365, #1374
- Both deploy pipelines serialise at workflow level, not per job — #1228
- Integration tests run on pushes to `develop`/`master`, not only on pull requests — #1228
- Preprod tracks `develop`, has its own Riot API key, and is deliberately tiny — a new key forces an empty database — #705
- Preprod builds carry a prerelease version, tagged only after they deploy (2026-08-24)
- Caddy terminates TLS and is the only public entry point in prod — #433, #430, #426
- The admin `/analytics` iframe stays on Umami's public share view, not the authenticated app — kept as-is on purpose, 2026-08-04 — #1013, #1014
- Umami session replay/heatmap rows are purged after 7 days by a sidecar container, not left to grow — #680, #1018
- `/ops/*` is the only authenticated API surface
- The Riot API key is a permanent *personal* key — not a 24 h dev key, and not production-approved — #532, #780

## Admin portal — observability data — [`decisions/admin-observability.md`](decisions/admin-observability.md)

- Admin-portal data lives in Mongo, not Postgres (2026-08-01) — #416, #93, #925
- The disk figure sums Postgres and Mongo, because there is one disk — #1023
- A step change in what is measured is not growth, so the forecast restarts at it — #1023
- Ingestion throughput is measured from the run summaries, not from `matches.CreatedAtUtc` — #1025, #982, #988
- The counters are summed in memory because the summary is stored as opaque JSON text — #1025, #990
- The candidate funnel measures Validated and Demoted, because Rejected is a status nothing assigns — #1024, #1029
- A forward-only counter renders as absent, not as zero, and key presence is what says which — #1024, #924
- `ValidatedAtUtc` had never been written in production, and the queue-latency snapshot is why that surfaced — #1024
- Queue latency is a snapshot over retained rows and is labelled as one, rather than being faked into a series — #1024
- Daily storage snapshots go to Mongo and are keyed on the day, not the run — #925
- The disk forecast is absent rather than approximate when the data can't support it — #680, #925
- Logs and metrics live in MongoDB, not Postgres, with two different guarantees — #416
- Ops logs are signal-only: Polly retry noise is not persisted, domain events are — #444, #93
- Riot API caller attribution uses an `AsyncLocal` ambient context, mirroring `IterationContext` — #1035
- The budget-headroom estimate (#1035) requires 24h of rollup history before it will extrapolate, and picks the app rate-limit window with the smallest daily ceiling as "binding"
- The configuration viewer is an allow-list, and each host reports itself (2026-08-08) — #1034, #1033, #924
- The pipeline chain is drawn per lane, not as one flat list (2026-09-02) — #1399, #1362

## Admin portal — health panels, charts and vocabulary — [`decisions/admin-health-and-charts.md`](decisions/admin-health-and-charts.md)

- A health panel may not pass what it did not measure — #924
- A detector shares the repair's definition of the bug it audits — #924, #911
- Detector thresholds are configuration, not constants
- A health panel answers before it reports — #992
- The health cockpit (`/health`, #1031) holds no depth of its own — every tile is a link, and the verdict is judged server-side
- A process that has never recorded a run is `unknown`, not amber; an abandoned run is a warning, not an error — #1031
- A chart's mark is chosen by what the series measures: flows are bars, stocks are lines (2026-08-25) — #924
- Admin bar charts go through a wrapper, because vue-chrts' bar tooltip is broken twice (2026-08-25) — #1218
- The admin portal has one status vocabulary and one duration ladder (2026-08-28) — #924, #1024
- The admin's tracked-region list stays a checked-in constant, not a read of `/ops/configuration` (2026-08-28) — #1249

## Workflow conventions — [`decisions/workflow-conventions.md`](decisions/workflow-conventions.md)

- Language split
- `develop` is the default branch
- Branch names
- A PR is done
- CI traps — #1236
- API wire conventions
- Every issue goes on GitHub Project #2

## CI runs only what the diff can break, and config files carry no comments (2026-09-02)

Every PR used to fire 13 jobs whatever it touched: a web-only change still built the .NET solution, ran the
Testcontainers suite, built all four images and validated four compose files. `ci.yml` now opens with a
`changes` job (`dorny/paths-filter`) whose outputs gate every other job and feed the frontend and Docker
matrices as JSON; pushes to `develop`/`master` stay exhaustive because those are the commits that deploy,
and docs-only PRs get no CI run. The two deploys share one reusable `rollout.yml` (migrate → deploy) and one
composite action generating the migration script, so the script CI validates is the script CD applies, and
preprod gained the same loud `preflight` as prod instead of a silent skip. The Claude review prompt is
English, defines "blocking" narrowly and forbids flagging versions or APIs from memory (the bot had called
`actions/checkout@v6` nonexistent). Dependabot groups minor+patch per ecosystem, majors stay separate. The
workflows, Dockerfiles, `dependabot.yml` and compose files carry **no comments**: their rationale lives in
`docs/ci.md` (and the ingestor knobs in `docs/prod.md`), where it can be read as one page instead of being
scattered across YAML. `docker compose config` renders byte-identical before and after the strip — #1386.

## Keeping these files current

A PR that ships a user-facing feature, removes one, or reverses a decision here **must update `features.md` and the
decision log in the same PR**: the full entry goes into the relevant `decisions/<area>.md` (new file only when no area
fits), and one line for it goes into this index under that file. These files are the context a fresh session loads
instead of re-reading the codebase; stale entries are worse than missing ones.
