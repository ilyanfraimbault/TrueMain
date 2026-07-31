# TrueMain — shipped feature inventory

What already exists, so a session doesn't propose it as new or re-derive it from the code.
Companion file: [decisions.md](decisions.md) (the *why*). Keep both updated — see the convention at the bottom.

Last verified against `develop` on 2026-07-28.

---

## web/ — public site (Nuxt 4 + Nuxt UI v4)

No `layouts/` dir; the shell is `web/app/app.vue` (`AppBackdrop`, `AppHeader`, `NuxtPage`, `AppFooter`).
All data goes through the catch-all proxy `web/server/api/[...path].ts` → `NUXT_API_BASE_URL`.

### `/` — `web/app/pages/index.vue`
Hero + search field, three live stat chips (champions ranked, main games analysed, truemains tracked), a tier-list teaser (`home/TierlistPanel.vue`, top 8, intersection-gated), a truemains teaser with region switch (`home/TruemainsPanel.vue`), CTAs.

### `/champions` — `web/app/pages/champions/index.vue`
Champion/lane directory, one row per (champion, position). Filters: role, champion, elo, patch. Each row shows keystone + secondary tree, consensus build path (6 items max), tier badge, WR/PR/**BR**.
**BR (ban rate, #920)** is lane-independent — the same value on every row of a champion — and shows an em dash, never `0%`, on patches predating ban ingestion. It does **not** feed the tier score.
Pagination and search are **client-side** (the endpoint returns the whole ~500-row directory, `PAGE_SIZE = 50`). Body wrapped in `<ClientOnly>` with skeletons; all fetches `server: false` deliberately (hydration-mismatch fix, #149).

### `/champions/tierlist` — `web/app/pages/champions/tierlist.vue`
Server-computed tier list. One `SectionCard` per tier group (S→D), each a grid of champion chips linking to the champion page. Filters: role, elo, patch. Each chip's stat line reads `WR · PR · BR` (same ban-rate caveats as the directory).

### `/champions/:id` — `web/app/pages/champions/[id].vue`
The richest page — two columns at `xl`.

- **Build tabs** (`Champion/BuildTabs.vue`) — one pill per build, each panel (`Champion/BuildPanel.vue`) containing:
  core section (spells, starter items, skill order, boots, build path, runes), **variations**, **build tree**, **rune variations**, and **power spikes** (`BuildPanel/Powerspikes.vue` — hand-drawn bars, Items/Levels toggle; only when both `championId` and `position` are set).
- **Below the fold**, all lazy + `hydrate-on-visible` with frozen props (`useLazyHydrationSnapshot`, the #834/#837 scroll-jank fix):
  - `Champion/TrendChart.vue` — **winrate & pickrate by patch**, two area charts over the last 5 patches (cross-patch by design; the patch filter is not forwarded). A third **ban rate** chart appears only once at least two patches carry ban data (#920); its series is filtered to those patches rather than drawn with gaps.
  - `Champion/PatchDiff.vue` — **patch diff**, two patch selectors + signed winrate swing, comparing core build / runes / skill order. Hidden when fewer than 2 patches exist.
  - `Champion/ScalingChart.vue` — winrate by game-length bucket + signed scaling index with verdict badge.
  - `Champion/Roam.vue` — out-of-lane K+A at 5/10/15 min with verdict. Hidden for `JUNGLE`.
  - `Champion/Synergies.vue` — **duo & trio synergies** (#922). Best teammates ranked by *observed − expected* win rate (never raw pair WR), with a `RolePicker` narrowing the partner lane (the champion's own lane is excluded). Picking a partner expands the best third picks for that duo underneath. Every row carries its sample; the three insufficient-sample cases (no champion baseline / no partner past the shared-games floor / duo too thin to split) each get their own sentence with the real count — no fallback numbers.
- **Sidebar**: top-10 truemains on the champion; **matchups** (best 5 / worst 5 + specific-opponent picker) with a **Lane / Game** win-rate split (#919 — lane WR counts only *decided* lanes, i.e. a gold gap past `LaneOutcomeAggregation:GoldLeadThreshold` at 15 min, so its sample is smaller than games and shows an em dash rather than 0% when no lane was decided; absent on the opponent-search path, which is a live query with no lane data); **mains comparison** (`Champion/MainsComparison.vue` — enter a Riot ID, compare WR/KDA/CS·min/gold·min against all mains or one chosen main).
- **Matchup filter** (#923): an opponent picker sits in the filter bar next to role/elo/patch (`?vs=<championId>`), and picking one **re-slices every build section** — variations, core, build tree, runes, skill order — from the games where the two champions actually met. It is not an aggregate read: `champion_matchup_stats` has the opponent but no build data, and the pattern aggregates have the build data but no opponent, so this path folds `match_participants` live (`ChampionMatchupBuildsQueryService` + `LiveBuildVariationAggregator`, sharing `ParticipantBuildFactsLoader` with the `/matchup` tool so a game means the same build to both). The core moves with the slice because it *is* the most played value inside it. Selecting an opponent pins the displayed position (the backend matches both sides on it and 400s without one); a matchup with no game in the retained window renders the no-data state. Every variation shows its game count — measured on production, the median champion-vs-opponent pair holds 4 games on a patch, so the volume has to travel with the percentages. **Power spikes** re-slice too (#957), but by a different route: their per-game magnitude needs the dense per-minute grid that retention prunes, so nothing can recompute them live — instead the aggregation now records the lane opponent it already measured each spike against, and the read filters on it (`?opponentChampionId=` on `/powerspikes`, no games floor, same 2026-07-30 rule). Two consequences to know. Only matches folded since #957 carry an opponent, so a matchup's spikes start empty and fill in patch by patch, and the section says *"No game of this matchup has been measured for power spikes yet"* rather than blaming the matchup. And the two halves of the page count **different populations**: the live build fold takes every participant who faced the opponent, while the powerspike aggregation only ever folded *tracked* accounts (`p1.Tracked`), so the spikes' game counts sit below the build tab's on the same matchup. Pre-existing — #923 is what widened the build path — and left as is rather than narrowing the builds or widening the aggregate, but each spike carries its own count so neither number is presented as the other's.
- States: `noDataForRank`, `notEnoughData`, low-sample alerts, and a watcher that reconciles the URL when the API drops a dead filter.
- **Share** (#926): `ShareButtons.vue` in the header of the populated state (copy link / native share / X), plus a dynamic OG card (`OgImage/Champion.satori.vue` — portrait, tier letter, WR/PR/BR tiles, sample and patch). See the *Share cards* entry under Cross-cutting.

### `/matchup` — `web/app/pages/matchup.vue`
A **live matchup tool** (reworked by #921, renamed from `/builder` by #939 — the old route still redirects, query preserved), not a build editor. Centre stage (`builder/MatchupStage.vue`): your champion, your role and the **role opponent** as large portrait-sized pickers — the matchup is the primary input because the API treats the pinned role opponent as a hard filter on the sampled games. The copy says *role* opponent, never *lane* opponent: a jungler has no lane (#939). Below it, secondary and quieter (`builder/TeamContext.vue`): the eight remaining draft slots (4 allies / 4 enemies, the played role omitted since the stage owns both sides of it), which only re-weight the similarity search — with no helper line, the section reads as optional on its own. The component folder keeps its `builder/` name on purpose (`components/matchup/MatchupStage.vue` would auto-resolve to `<MatchupMatchupStage>`).
All three matchup inputs are **deep-linked** (`?champion=&position=&opponent=`) so a matchup is shareable; the eight context slots stay ephemeral. No submit — every edit refires after a 400 ms debounce through `useCompositionBuild()` → `POST /api/champions/{id}/composition-build`, keeping the previous result dimmed on screen and dropping out-of-order responses.
Output (`builder/RecommendationPanel.vue`): confidence strip (games used, draft match %, win rate), low-confidence warnings, the same core panels as the champion page, and the build tree. **No power spikes, no variations, no rune list, no situational-items row** — #939 removed the latter (front *and* API) because the build tree already shows the same off-core items with their ordering.
The "Games used" stat has a light icon button (`i-lucide-list`, `UTooltip`-explained) opening a `UDrawer` — `builder/GamesDrawer.vue` (#940) — that lists the sampled games one `match/MatchRow.vue` row at a time (static, no `nameTag`: the ten rows come from ten different pilots, so no single row expands into a detail panel), each annotated with its similarity score (`Score / MaxPossibleScore`, the ratio "Draft match" averages) and the piloting Riot account (name#tag, profile icon, a "Main" badge when the selection's mains-first tier picked them). Ordered exactly as the selection ordered them — mains first, then best score, recency breaking ties — the drawer paginates that order rather than re-sorting it, fetching lazily on open via `useCompositionBuildGames()` → `POST /api/champions/{id}/composition-build/games` (same draft body as the recommendation) so a panel most visits never open doesn't pay hydration cost on every 400 ms debounce. Backend: `CompositionRecommendationQueryService.GetGamesAsync` re-uses the selection cache the recommendation itself populated (a 30 s in-memory entry keyed on the normalised draft) and hydrates the requested page through `CompositionGamesQueryService` + the shared `MatchSummaryHydrator` (extracted from `MatchSummariesQueryService` so the truemain match feed and this drawer render a game as the identical row).
Graceful degradation, both falling back to `builder/FallbackBuild.vue` (the champion page's top build) behind an explicit notice: the matchup was **never recorded** (`matchupFound === false`), or it was recorded on **fewer than 8 games** — the thin build stays one click away ("Show it anyway" / "Back to the standard build").

### `/truemains` — `web/app/pages/truemains/index.vue`
OTP leaderboard, server-paginated (25/page). Filters: region, role, OTP-only toggle, and **sort by LP or dedication**. Rows show rank, name#tag + region flag, lanes, top-3 champions, main's keystone + first item, dedication score with a breakdown tooltip, rank icon + LP, and a favorite toggle.

### `/truemains/:nameTag` — `web/app/pages/truemains/[nameTag]/index.vue`
`ShareButtons.vue` sits on the breadcrumb row in every state, next to a dynamic OG card (`OgImage/Truemain.satori.vue` — Riot ID, rank + LP, W-L, main champion, dedication / games / ranked WR tiles), #926.
Left rail: profile header, **ranked card**, dedication card, main champions, position breakdown.
- **`ProfileRankedCard.vue` — the LP / rank-history curve** (`useTruemainRankHistory.ts` → `/api/truemains/{tag}/rank-history?days=90`). Headline rank + W-L-WR, **"Last 30d"/"Last 7d" LP delta badges**, an area chart with **one series per tier** so the line colour-shifts at each promotion/demotion, and a hand-positioned tier-crest gutter.
- **`ProfileActivityHeatmap.vue` — the activity grid** (#927, restyled by #959), directly under the LP curve. Fixed 11px squares (GitHub-contribution size, `auto-fill` columns — wraps rather than stretching) with a four-way mode switch — **game** (last 60), **day** (~30 UTC days), **week** (up to 12 ISO weeks), **patch** — over a single payload (`useTruemainActivity.ts` → `/api/truemains/{tag}/activity`, no mode param, switching is local). Rosegold-400 fill for a winning period, mauve-400 for a losing one (`utils/activity-heatmap.ts`) — kept inside the app's rose-gold-only palette instead of the match feed's sky blue; alpha is half decisiveness, half volume, floored well above the empty tile's own alpha so a single loss never fades weaker than "no games". **An empty period gets its own faint fill** (`bg-white/6`), not a fill-less outline — the outline alone dissolved into the `.glass` card, which read as an idle cell when it was actually just camouflaged; its tooltip says "No games". A single shared hover tooltip (not 60 poppers) follows the pointer over the grid and always prints wins/games (`W/G · rate%`), never "Victory"/"Defeat" wording, so every cell shares one shape. #959 dropped the standing coverage line described in the retention-asymmetry entry in `decisions.md` — see that entry for why.

Right rail: match history (role + champion filters, 20/page, URL-backed). `MatchRow.vue` is an accordion; expanding mounts `match/MatchDetailPanel.vue` with three tabs — General (scoreboards), Details (per-player panel), Runes. The collapsed row prints the **performance score** (`{n} PERF`, tooltip carries the 1..10 placement) next to CS/m and KP, and its MVP/ACE crown now comes off the real scorer rather than a KDA proxy (#918).

### `/truemains/:nameTag/champions/:id`
Player-scoped mirror of the champion page. Adds **`Champion/MainsDivergence.vue`** — "{player} vs mains" across starter / boots / core items / skill order, each with a differs/matches badge and a coaching line built from real rates — and **`Champion/PlayerPerformance.vue`** (#918): the average performance score over the player's last 20 ranked games on the champion, with best / worst / top-of-team rate and a per-component bar breakdown (each component labelled with the games it was actually available in). Suppressed below 5 graded games, with the real count in the copy. Sits outside the build region so it still renders in the no-build degraded state, lazy + `hydrate-on-visible` with frozen display names. Matchups here are scoped to the player's games. **No** trend / scaling / roam / patch-diff / power-spikes / truemains panel.

### `/truemains/favorites`
localStorage-backed follow list (`web/app/utils/favorites.ts`, key `truemain:favorites:v1`, hard cap 30, oldest-first eviction). Per-player cards with their latest 3 matches. `noindex`, excluded from the sitemap. No account sync (no RSO).

### `/privacy`, `/terms`
Static legal prose — required for the Riot production-key application.

### `/dev/*`
Component playgrounds (`charts`, `match-row`, `profile`). **Stripped from production** by a `pages:extend` hook in `nuxt.config.ts`.

### Cross-cutting (web)
- **Search / command palette** — `AppSearch.vue`: one modal + lazy `UCommandPalette` (keeps Fuse out of the site-wide bundle, #832). Groups: champions (local Fuse), truemains (debounced server search), browse shortcuts. ⌘K bound only on the header instance. Two variants (`field`, `button`); `champion-mode="filter"` makes selection filter instead of navigate.
- **Design system** — primary `rosegold`, neutral `mauve` (`app.config.ts`). Every `UCard` is `glass rounded-2xl` + `soft`. Utilities `glass`, `glass-hover`, `deselected`, `selected-perk` and the tier ladder live in `web/app/assets/css/main.css`. Text-hierarchy conventions: `web/docs/DESIGN_SYSTEM.md`.
- **Backdrop** — `AppBackdrop.vue`: fixed WebGL rose-gold "eclipse", cursor-reactive, degrades to a CSS wash, respects reduced-motion.
- **Game tooltips** — `GameTooltip/*` + the Riot-markup parsers in `web/shared/utils/tooltip-parser/`.
- **Charts** — `nuxt-charts` (Unovis) wrapped by `components/charts/{Area,Line,Bar}Chart.vue`. Only `AreaChart` is used in production; power spikes and roam draw bars by hand on purpose.
- **SEO** — `@nuxtjs/seo`, title template `%s · TrueMain` (never repeat the brand in a page title), schema.org on the main pages, sitemap enumerating champions and leaderboard players.
- **Share cards** (#926) — `nuxt-og-image` is **enabled**, Satori + `@resvg/resvg-js`, 1200×630, 1 h cache. Exactly two templates, `app/components/OgImage/{Champion,Truemain}.satori.vue`, styled with inline objects off `OgImage/theme.ts` (Satori reads neither CSS vars nor Tailwind). Only those two pages call `defineOgImageComponent()`; everything else keeps plain og:title/og:description. The templates receive **identifiers only** and resolve their own numbers through `server/api/og/{champion/[championId],truemain/[nameTag]}.get.ts` (1 h `defineCachedFunction`), because both pages fetch client-only. Missing data degrades in place — see decisions.md.
- **Share buttons** — `ShareButtons.vue`: copy link (with an `execCommand` fallback for insecure contexts), native Web Share (mount-gated, mobile only), and an X intent anchor. The link is rebuilt from the live route, so it carries the filters currently applied.
- **Caching** — 1 h client TTL mirroring the server `defineCachedFunction` (`utils/static-cache.ts`), static prefetch plugin, and a custom cached IPX handler for `/_ipx/**`.
- **Analytics** — self-hosted Umami, only injected when both public env vars are set.
- **Dev mock** — `NUXT_DEV_MOCK_API=1` → `web/server/utils/dev-api-mock.ts`, deterministic PRNG, real Riot ids, covers most endpoints; a dedicated fixture takes precedence for `Sheiden-1234`. Both the mock and the Sheiden fixture reproduce #927's **retention asymmetry on purpose** (match-sourced series ~24-26 days deep, patch series the whole career and summing to the dedication card's `careerGames`) — four equally deep series would hide the one property the card's copy is about. The matchup page's degraded states are reachable through the role opponent's id: `% 23 === 0` (Tryndamere) returns a never-recorded matchup, `% 17 === 0` (Teemo) a 3-game one.
- **No i18n** — all copy is hardcoded English; numbers use an explicit `en-US` locale to avoid hydration mismatches.
- **Not present**: no gold/XP timeline chart in match detail, no auth/accounts. Share cards exist only for `/champions/:id` and `/truemains/:nameTag` — the tier list, the builder and the leaderboard have none.

---

## admin/ — operator portal (standalone Nuxt app, `ssr: false`)

Separate app with its own deployment and domain — **not** a `/admin` route of `web`.

**Auth & backend access.** Single-operator username/password → sealed httpOnly session (`nuxt-auth-utils`); no user store, no roles. `admin/server/api/auth/login.post.ts` does a constant-time compare with a per-IP throttle (5 attempts/60 s). Every route is gated by `admin/app/middleware/auth.global.ts`.
**All backend traffic goes through one proxy**: `admin/server/api/ops/[...path].ts` requires the session, then forwards to `${opsApiBaseUrl}/ops{path}` injecting the secret `X-Ops-Key` **server-side** — the ops key never reaches the browser. A boot plugin refuses to start outside dev with default credentials or short secrets.

| Route | What it shows |
|---|---|
| `/` | Overview: stat cards (tracked accounts, matches, participants, mains/OTPs, candidates by status), matches-over-time area chart with week/month/year/patch granularity, top-10 champions bar chart |
| `/champions` | Per-champion games/mains/OTPs, filters region/patch/position/queue, sortable table + 2 bar charts. Surfaces the caveat that mains/OTPs honour **region only** |
| `/database` | Current table sizes (row estimate, total/table/index bytes, name filter, sortable table, top-12 bar chart) **plus growth history and a disk forecast** (#925): database size over time with a 30 d/90 d/1 y window, estimated rows added per day, a per-table growth list (bytes/day, rows/day, % over the window), and projected crossing dates for configurable fill levels. History comes from daily Mongo snapshots, so widening the window costs the database nothing. The forecast is **absent rather than guessed** — under 3 days of history, flat/shrinking storage, or no configured `StorageHistory:DiskCapacityBytes` each render an explanation instead of a date. Mongo collection sizes are still not shown |
| `/data-quality` | **Read-only diagnostics.** Top section: **5 automated detectors** (#924) — duplicate dimension rows on the shared canonical key, aggregate freshness per fold (+ on-demand per-champion breakdown), orphan-participant share & Harvest lag, ingestion lag & queue depths, row-level sanity & patch volumes. Each card carries a green/amber/red/unknown verdict, its headline number, non-green rows by default, and the configured thresholds it judged against (`DataQualityDetectors:*`). Below it: the per-match checks (`missingTimeline`, `wrongParticipantCount`, `missingTeamPosition`, `zeroDuration`, `duplicateChampion`), one card per issue type with its own pagination, plus a match-ID slide-over showing both teams by position with missing/duplicate slots tinted. No repair actions |
| `/candidates` | `main_candidates` pipeline (New→Scored→Queued→Processing→Validated/Rejected) with search/filters + detail slide-over; and the manual seed-request intake list |
| `/processes` | Per-process rollup (last status/run/success, recent failures) + paginated runs table with the run `summary` JSON rendered by `ProcessSummaryView`; plus the pipeline-chain iteration view |
| `/aggregation` | Per aggregation family: exact row counts, distinct champions/patches, freshness, last run + summary, and the ingestion backlogs that should read zero when caught up |
| `/logs` | Two tabs (deep-linkable `?view=crashes`). **Logs**: severity/category/event-type/process/window/text filters, detail slide-over with exception stack, multi-select copy as JSON. **Crashes**: full report per row — plain-language explanation, exception chain, environment + memory/GC snapshot, recent log tail |
| `/riot-api` | Riot usage over 1h/24h/7d from the Mongo rollups: totals, per-endpoint table, status-code histogram, call-volume chart, latest rate-limit snapshot |
| `/analytics` | Iframe onto the self-hosted Umami. 54 lines, no backend call |
| `/seed` | "Add mains": single-add form with a Pending→Resolving→Ingested/Failed stepper (polls ~2 s), bulk-add textarea (`name#tag[,REGION]`) with dedupe + malformed-line flagging + progress bar, shared history table |

Tests: `admin/tests/` covers only `process-summary`. No page-level or proxy tests.

---

## backend/ — .NET 10 solution

### Api
Three controllers, all delegating to injected `I*QueryService` — no EF types cross the controller boundary; read models are `sealed record`s under `Api/ReadModels/`.

- **`ChampionsController`** (public): list, tierlist, detail, trend, patch-diff, matchups, synergies, synergies/trios, scaling, item-timings, roam, powerspikes, mains-comparison, `POST composition-build`, and `POST composition-build/games` (#940 — the recommendation's provenance drawer, paged).
- **`TruemainsController`** (public): search, leaderboard, profile, player-scoped champion + divergence + matchups + **performance**, rank-history, **activity** (#927 — the four-granularity grid in one payload, no mode param), matches, match detail.
- **`OpsController`** (`X-Ops-Key` auth): 18 endpoints backing the admin portal.

Cross-cutting: memory cache (`SizeLimit = 1024`, no Redis), global per-IP rate limit 100 req/min, `/healthz` + `/readyz`, RFC 7807 problem details with `traceId`, HSTS outside dev, CORS that fails boot when unset outside dev, OpenAPI/Scalar **Development-only**.

> Reads live in `Api/Services/<area>` as purpose-built query services injecting `TrueMainDbContext` and projecting read-models with `AsNoTracking` — no generic repository. That is the rule as decided in #865, not a divergence from it; see [decisions.md](decisions.md#where-api-reads-live).

### Ingestor
`Worker.cs` is a `BackgroundService`: reconcile orphaned `Running` rows to `Abandoned` at boot, then loop {heartbeat file → `RunOnceAsync(mode)` → stop if `Job:RunOnce` (default true) else wait `Job:IntervalMinutes`}. A failed iteration is logged and counted, never fatal. The mode concept is **`JobMode`** — `Full` expands to the ordered pipeline below, any other value runs that single process. Each process is DI-keyed by its `JobMode` and wrapped in `RecordedProcess` (writes `Running`, heartbeats, then `Success`/`Failed` + serialized summary).

Pipeline order (`Full`):

| # | Process | What it does |
|---|---|---|
| 1 | Discovery | Walks Master/GM/Challenger ladders per platform → accounts, mastery-derived candidates, rank snapshots |
| 2 | ManualSeed | Drains pending seed requests (admin "Add mains"), promotes straight to `Queued` |
| 3 | Harvest | Generates candidates from orphan `match_participants` — zero Riot calls |
| 4 | Scoring | Weighted blend (recency, rank, mastery, champion scarcity) → top-N promoted to `Queued` |
| 5 | MainActivity | Retires/reactivates mains via champion-mastery `lastPlayTime` (1 call/account); flags rows, never deletes |
| 6 | MatchIngestion | Lease-claims accounts, fetches match-v5 + timeline, writes matches/participants/snapshots/kill positions/jungle clears/perks |
| 7 | MatchTeamPositionCorrection | Backfills `team_position` for the unambiguous single-gap case |
| 8 | MainAnalysis | Computes `main_champion_stats` (play rate, mains/OTP) with adaptive thresholds + demotion policy |
| 9 | EloBracketEnrichment | Stamps `elo_bracket` from the nearest rank snapshot |
| 9b | RunePageDeduplication | Collapses permutation-duplicate `champion_dim_rune_pages` rows and normalises every remaining row's secondary perk order (#911). Runs **before** the aggregation so a pass never aggregates into a dimension it is about to rewrite; drains once, then a no-op |
| 10 | ChampionPatternAggregation | Rebuilds aggregate scopes/patterns + `champion_dim_*`, **chunked one champion at a time** (memory bound) |
| 11 | ChampionMatchupLeadAggregation | Incrementally folds each match once into `champion_matchup_stats` |
| 11b | ChampionSynergyAggregation | Folds each match once into `champion_synergy_stats` + `champion_synergy_baseline_stats` (same-team pairs and their SELF/ALLY marginals) |
| 11a | ChampionLaneOutcomeAggregation | Judges each match's lane from the 15-min timeline snapshots and folds three counters (`LaneGames`/`LaneWins`/`LaneLosses`) into the same `champion_matchup_stats` rows (#919). Runs right after the matchup fold, over the same matches |
| 11c | ChampionBanAggregation | Folds each match once into `champion_ban_stats` + `ban_scope_totals` (ban counts and the match totals they divide by). Must run **after** elo enrichment — the fold is one-shot and decides there which bands a match counts in |
| last | StorageSnapshot | Records the day's `pg_catalog` sizes + measured `pg_database_size` into `db_table_size_snapshots` (#925). Runs **after** retention, so the figure is the steady-state size rather than the pre-deletion peak |
| 12 | ChampionPowerspikeAggregation | Folds each match once into the powerspike stat tables while dense snapshots still exist; event rows are keyed on the lane opponent the spike was measured against (#957) |
| 13 | AccountRefresh | Refreshes identity + soloQ rank; recovers or invalidates dead PUUIDs |
| 14 | MatchDataRetention | Prunes stale candidates, non-tracked-queue matches, out-of-window matches, intermediate timeline snapshots; rolls a frozen patch's per-opponent powerspike rows back into one (#957) before applying the sub-floor powerspike prune, which must see the rolled-up games |

**Riot client layer** — typed clients (match / platform / account) each wrapped in a resilience handler (rate limiter, total timeout, retry honouring `Retry-After`, circuit breaker, per-attempt timeout) with the metrics handler **inside** it, so every physical attempt including retried 429s is recorded to Mongo.

### Data
`TrueMainDbContext`, 26 `DbSet`s. Tables by domain:
- **Identity**: `riot_accounts`, `personas` (half-built, unused), `main_candidates`, `seed_requests`, `discovery_cursors`
- **Raw matches**: `matches`, `match_participants`, `match_participant_timeline_snapshots`, `match_participant_kill_positions`, `jungle_first_clears`, `participant_perk_selections`, `perk_selection_catalog` (item/skill events are jsonb, not tables)
- **Aggregates**: `champion_aggregate_scopes` (account × champion × patch × platform × queue × position × elo bracket) + `champion_aggregate_patterns` (one row per observed build+runes+skills+spells+starters combo), with content-deduplicated `champion_dim_{builds,rune_pages,skill_orders,spell_pairs,starter_items}`. Rune pages store their two **secondary** perks in canonical (ascending id) order — the player's click order made the same page two rows and split its sample (#911)
- **Derived**: `main_champion_stats`, `champion_matchup_stats`, `champion_synergy_stats` + `champion_synergy_baseline_stats`, `champion_powerspike_{curve,event}_stats`, `powerspike_sigma_stats`
- **Snapshots / ops**: `rank_snapshots`, `process_runs`

**Mongo** (`truemain_logs`, TTL retention): `logs` (90 d, lossy bounded channel), `audit_events` (lossless, synchronous), `riot_api_call_rollups` (14 d), `crashes` (365 d, file-first then Mongo, with unclean-shutdown detection for OOM kills), `db_table_size_snapshots` (365 d, one document per table per day, day-keyed upsert so the pipeline's many daily runs refresh rather than append).

**Compiled model** in `Data/CompiledModels/` is committed and auto-discovered (no `UseModel()` call) — regenerate with `dotnet ef dbcontext optimize` after any schema change.
**BuildFacts** (`Data/BuildFacts/`) is the build-derivation shared by Ingestor and API: item metadata provider, boots/final-build/starter resolvers, skill-order builder.

### Core
Dependency-free domain layer: `DedicationScore` (0–100 with exposed components), `MainAnalysisOptions` (the main-analysis contract shared by Ingestor and API), Riot identifiers/routing, map & queue types (`LolQueueId`, `LolPosition`, `QueueDataQualityProfile`, `TeamPositionInferrer`), ranking (`EloBracket`, `RankScore`), and **`MatchPerformanceRanker` / `PerformanceScore` / `PerformanceScoreInput`**.

> ⚠️ **A per-match performance score already exists and ships.** `MatchPerformanceRanker` is consumed by
> `Api/Services/Truemains/{MatchDetailQueryService,MatchSummariesQueryService,PlayerChampionPerformanceQueryService}.cs`;
> it is what produces the **MVP/ACE accolade** on collapsed match rows. Unit-tested in
> `tests/TrueMain.UnitTests/{MatchPerformanceRankerTests,PerformanceScoreTests,PerformanceInputsTests}.cs`.
> #918 must therefore **extend and surface** this scorer, not build a new one.
>
> **Shipped in the first #918 PR** — the model is documented in [`docs/performance-score.md`](../../docs/performance-score.md):
> nine role-weighted components (combat, KP, damage share, gold share, farming, vision, **laning** ≤15 min,
> **mid game** >15 min, **roam**), fed by the whole canonical-mark lead curve (5/10/15/20/30) instead of a
> single @15 diff, plus out-of-lane takedowns from `match_participant_kill_positions`. `PerformanceScore.Explain`
> returns the per-component breakdown. Input assembly is centralised in `Api/Services/Truemains/PerformanceInputs.cs`
> so the match feed, the detail page and the player-champion panel cannot grade the same game differently.
>
> **Still open on the epic**: the peer-relative radar — it needs a role + rank baseline aggregation
> (percentile per component within a bracket) that does not exist yet, and rule-based badges on top of the
> same inputs.

### Known dead / stale spots
- `GET /ops/pipeline-health` is implemented and tested but **no admin page calls it**.
- The `"ops"` rate-limit policy referenced in a `Program.cs` comment **does not exist** — ops shares the global limit.
- `SeedOptions` is bound but never injected (dead config; `ManualSeedProcess` uses `ManualSeedOptions`).
- `ChampionAggregateBuild/RunePage/SkillOrder/SpellPair/StarterItems` in `Data/Entities/` are transport DTOs, **not tables**.
- `personas` has no writer and no reader.
- `champion_timeline_lead_stats` was dropped in #889 but is still named in a `MatchDataRetentionOptions` doc comment.
