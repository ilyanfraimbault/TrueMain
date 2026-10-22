# TrueMain — decision log

Settled product and architecture decisions **with their rationale**, so a session doesn't re-litigate them or
propose something a past incident already ruled out. Companion file: [features.md](features.md) (the *what*).

This file is the **index**: one line per decision, grouped by the file that holds its full entry. Read the
index to know whether a decision exists, then open only the file you need — each holds the verbatim entries
(**Decision** — why — `source`) for its area. Do not paste a decision's body back here.

Last verified against `develop` on 2026-09-02.

## Mains, dedication and candidate intake — [`decisions/product-mains.md`](decisions/product-mains.md)

- Ranked solo/duo (queue 420) is the only queue stored; match history is solo/duo-only by design — #680
- The Truemain score (0–100) is the signature metric, always scoped to one champion — #530
- The score reads the player, not our tracking: play rate + Riot mastery, activity as a gate, `IsOtp` as the verdict — #1701
- The `/truemains` leaderboard is strictly `IsMain=true` — #184
- Leaderboard games/KDA/WR come from frozen aggregate scopes, not live `match_participants` — #719
- The Games / KDA / WR sorts rank a cached in-memory read of the whole board, not denormalised columns — #1737
- Inactive mains are retired via champion-mastery `lastPlayTime`; intake favours depth over breadth — #900
- Candidate scoring is scarcity-weighted and the `IsMain` threshold is coverage-adaptive (0.20 → 0.12) — #407
- Thin samples degrade, they don't 404 — #762
- The account explorer (#1032) reports raw score inputs, not score components — and never calls Riot
- `MainActivity` deactivation carries no persisted reason, so the account explorer says so rather than guessing — #900
- A main whose matches expired is dated, not deleted and not hidden (2026-08-24) — #825, #466
- A candidate has no rejection verdict: it is in the pipeline, demoted back into the pool, or pruned (2026-10-06) — #1029, #1024, #1535
- An ingested account without a main is re-analysed when a harvested game shows it playing (2026-10-13) — #2039, #1535, #825

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

## Champion page — builds, SSR prose — [`decisions/product-champion-page.md`](decisions/product-champion-page.md)

- The "why this item" card is a rate and a situation, nothing else: no contrast rate, no sample, no scope footnote — #1451, #1465
- The card's key term uses the item tooltip's colour vocabulary (damage coloured by the resistance that blocks it); untinted where no stat answers it — #1465
- A `Preference` verdict renders nothing on the card — #1465
- An item-context axis the front end has no wording for is dropped, never printed raw; a test mirrors the backend's axis list — #1451
- The build tree carries no situational mark: the ring competed with the main-path highlight — #1451, #1465
- A situational card is borrowed from another branch when the hovered step has no finding of its own — but only if the item has exactly one reading and the borrowed one rests on more games — #1518
- Champion timeline-leads ("Lead vs role opponent") was removed; matchups stayed — #889
- Power spikes were removed end to end (endpoint, fold, tables, per-minute snapshots) — judged not useful — #1599
- "Completed item" is the build path's eligibility rule, shared, not a local restatement of it — #1021
- Champion URLs are bare slugs, and the slug map is app state rather than per-page async data — #1124
- The champion page server-renders its build as prose — the one SSR round-trip the page pays — #926, #1123, #1273
- The build paragraph's cache window is 5 minutes, because it sits next to live numbers — #1123, #926, #1273
- `hydrate-on-visible` does not make a panel free — it defers hydration, not server rendering — #1123, #1231
- The build paragraph is typeset, and rune trees get Riot's colours to do it — #1123, #1143
- The paragraph's hover cards are resolved client-side, not carried in its payload — #1147, #1145
- Roaming is gone: no badge, no endpoint, no kill-position table — #536
- A variation card only exists when there is a variation; a settled build says so by being short — #1466
- The panel answers before it nuances, and the build tree is a picture, not a card — #1466
- The build paragraph is collapsed, moved to the foot of the sidebar, and no longer restates the icon grid — #1466, #1123, #1143
- The server HTML names the lane's best and worst matchups, and never a panel's empty state (2026-10-09) — #1954, #1538
- A verdict chip inside its own dead zone says nothing, so it says nothing — #1466
- The population the numbers come from is stated in the header, not hovered — #1466, #1346
- The patch diff is gone, front and API — the trend chart already shows patch-over-patch movement — #1466, #534
- No orphan card in the variations row, and the runes fill the height they are given — #1466
- A variation's badge is one number, and the precision is in the tooltip — #1469
- A variation chip's percentage is a share of its card, so it gets its own colour scale — white at rest

## Champion directory, tier list and served patch — [`decisions/product-directory-and-tiers.md`](decisions/product-directory-and-tiers.md)

- Tier score is presence-first (pick rate + ban rate, weighted above a sample-shrunk win rate) — this reverses #920's "bans don't feed the tier score"
- The static champion list drops Data Dragon entries with an id at or above 10 000 — alternate-mode kits, not champions — #966
- A champion gets at most two lines in the directory — its dominant lanes — and the cap is applied before tiering — #1082
- A tier-list chip is a portrait and its lane badge — the name and the three rates are tooltip content
- The tier list carries the directory's header and filter row, control for control (2026-10-01)
- A patch is served only once it can fill a directory (2026-08-12) — #1109, #1107
- The directory listing reads `GET /champions/directory`: one page, filtered and ordered in memory over the cached directory (one read-cache entry per ordering, sliced per page); the bare `GET /champions` array stays (2026-10-01) — #1734
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
- The live champion reads compose that same cohort; a read whose champion side differs on purpose (one player, the full build pool) still takes its matches from it, so no read counts a remake (2026-10-06) — #1365
- The matchups panel follows the page's patch filter on the global route, and deliberately does not on the player one — #1087
- One player's matchups carry no games floor, and the player champion page has an explicit "All positions" slice (2026-10-20) — #2158
- The champion page shows its matchups as a spread on the lane win rate at 15 min (centred on 50%), replacing the sidebar list — #2094
- The player champion page shows the same spread on the player's own lanes (live from their 15-min snapshots, no lane floor), replacing its sidebar list — 2026-10-22
- Lane win rate stores three counters and divides by the *decided* lanes, not by games played — #466, #919, #606
- A match's game and lane counters are folded in one pass, off one flag, because `elo_bracket` is mutable — #1445, #919, #1362
- The draft assistant's lane guess is an assignment solved by exact enumeration — not per-champion arg-maxes, not Hungarian — #1674, #1706
- Lane priors read the ally synergy baselines, not the scope table, decay over four patches rather than switching, and pool every elo band (2026-10-05) — #1674, #1706, #1707
- Draft candidates are ranked by measured deltas kept separate — lane (probability-weighted), blind safety, champion strength, mean synergy — shrunk by their games and weighed lane first by a back-test, never a fabricated win probability or a grade — #1675, #1706, #1906
- The draft's team composition is read from the item-context fold's ally-axis totals, no aggregate of its own; weight 0 until a back-test sets it, stated as a reason after the score terms meanwhile (2026-10-21) — #1906
- Draft ban suggestions protect the declared pick (else the mastery pool) from the lane opponents it is behind into, weighted by how often they are played; never an ally's champion — #1906
- Draft reads use the current patch with the previous one as a per-champion fallback, not every stored patch — #1906
- The draft's enemy team is its own opposing-pair aggregate behind its own match flag, weight 0 until a back-test sets it — #1713, #1906
- A matchup-scoped build page is folded live, not aggregated — #923, #1075, #1098
- The draft tool is the "Matchup" page (`/matchup`), and its opponent is the *role* opponent — #939
- The recommendation shows no situational-items row — #921, #939
- The matchup tool judges the lane over its own sampled games — this finishes #1111's merge
- `/matchup` carries one line of numbers, not two — and it stores the XP gap beside the gold one — #1098, #976, #1087
- Column captions head every rendering of the matchup rows, filtered view included, and beat figure-restating tooltips — #1494
- The role opponent is a database filter, a pinned matchup is used whole, and votes are weighted by patch and pilot — #1659, #563, #923
- A refetch on `/matchup` shows skeletons rather than the previous answer dimmed — #1659, #1117

## Player profile — [`decisions/product-player-profile.md`](decisions/product-player-profile.md)

- The activity grid has one unit — the UTC day — and the switch picks the window, not the unit — #1486, #1473, #927
- The patch window is measured over everyone's matches, never over the profile's own history — #1473
- The month window is clamped to the retention floor; the narrower ones need no clamp — #1483
- The grid is squares and nothing else: no captions, no legend, no coverage line — #1479, #1473
- An idle day is not a lost one, and every day of the window is drawn — #1473, #907, #927
- The player performance panel shows the score and its sample only — no per-component breakdown — #918
- The rank chart changes colour where the curve crosses a tier threshold; GM / Challenger thresholds are measured cutoffs, the days before the first one estimated from tracked snapshots (2026-10-20, revises 2026-08-20; estimates 2026-10-21) — #2174, #2181
- A Riot ID resolves case-insensitively, in exactly one place (2026-08-26)
- The "{player} vs mains" card is gone — one page, one definition of a core build (2026-09-03) — #529
- An empty slice keeps its filters; the header degrades instead of disappearing (2026-09-03)
- The site's match detail has no Timeline tab: TrueMain computes and stores no win-probability curve, the desktop app alone builds it from the client's timeline (2026-10-12, reverses 2026-10-05) — #2009, #1911
- Known players: one person, several accounts at `/players/{slug}`, linked only from public sources, never inferred (2026-10-13) — #2016
- Known players' accounts on a platform the pipeline does not ingest are stored as `untracked`, never seeded (2026-10-19) — #2134
- A main champion row reads its lane and play rate only: the `80 of last 200` sample line is gone (2026-10-13, reverses #1498's line)

## SEO, share cards and OG images — [`decisions/product-seo-and-sharing.md`](decisions/product-seo-and-sharing.md)

- Share cards degrade in three steps and never print a number the API did not return — #920, #926
- Share cards resolve their own data server-side instead of receiving it from the page — #149, #926
- The champion link graph was server-rendered, then removed — only the matchup sentences' six links remain — #1123, #1209, #147, #1954
- A platform-dependent `UKbd` cannot be server-rendered — #1209
- OG image rendering is on, pinned to Satori + resvg, and deliberately reaches exactly two pages — #551, #926, #600
- A cold OG render gets 30 s, and the fix is the wait, not a prerender or a warm-up (2026-10-04) — #1545
- OG image URLs are signed with a secret regenerated at every build, and that is left as the default — #926
- The sitemap advertises champions, not truemain profiles; known players with a tracked account are in it, their page being server-rendered (2026-09-01, extended 2026-10-14) — #862, #1337, #551, #2018

## Web frontend rules (hydration, fetches, icons, tooltips) — [`decisions/web-frontend-rules.md`](decisions/web-frontend-rules.md)

- "Client-only fetch" has to be enforced on the *side*, not merely intended — an immediate watcher is not client-only — #862, #1234
- A closed `enabled` gate resolves `success` with an empty model, so the gated composables expose their own `pending` — #1234
- Every hand-rolled fetch composable carries a monotonic request token — #1234
- A backend request nobody waits for any more is cancelled: every fetch forwards an abort signal (2026-09-27) — #1712
- `onServerPrefetch` stays in the client bundle so `useId()` agrees across hydration; a table column never renders an empty-string header (2026-10-04) — #1590
- A row rendered on more than one surface sizes off its own width, not the viewport — #967
- The full-page lists (truemains, champions) are `UTable`s with header sorting bound to the URL and served by the API; a row navigates through `@select` (2026-10-01) — #1726, #1734
- A player is drawn by one `Account` component (UUser + a canonical-URL `UAvatar`, rounded square) on both apps; the search palette is the exception; a Riot ID is written `gameName#tagLine` (2026-10-01, 2026-10-13) — #1734, #2027
- A match is drawn by one `MatchRow` (the profile's 54 px design, one component per column, `@container` tiers from 320 px) and every list loads with `MatchRowSkeleton`; the open game reuses its columns and its result colours (`result-tone.ts`) (2026-10-13) — #2020, #2030
- A day-grouped match list is drawn by one `MatchHistoryList` (skeletons, empty state, day headings, pager; hosts keep their headings and filters, row controls through its `actions` slot) (2026-10-13) — #2024
- Every stat has one formatter: KDA (`kda.ts`, two decimals, `Perfect`), signed deltas (`deltas.ts`, rate moves in `pts`), percentages, counts, clocks, champion names and positions (2026-10-13) — #2031
- A tooltip trigger keeps the same DOM element for the life of the component
- Game-entity hover cards open above their icon, flipping below only when they must (2026-09-25) — #1698
- A champion page builds only what is on screen: hidden build tabs and unhovered tooltips wait (2026-09-15) — #1585
- A skeleton is the real component in `pending` mode, not a drawing of it
- Icon slots are rendered from the ids, never gated on a resolved static lookup
- Champion-page icons are slow because of browser queue depth, not the image proxy — measure the split before "optimising" it — #680, #997
- `SkeletonImage` serves WebP; `RankIcon` deliberately does not
- Every picture shows a skeleton until it has loaded (`SkeletonImage`, `SkeletonPicture`, or an opaque `<img>`'s own `img-skeleton` background for a match row's ten composition icons, #2129); build-summary inline icons excepted (2026-10-06) — #1949
- Every icon URL is built by one helper, so one asset is one cache entry — #1000
- The `/_ipx/**` cache evicts by patch, keeping the current patch and the two before it — #997
- `web/` and `admin/` share their server code and Data Dragon helpers through `web/layers/nitro`, which the admin extends through a second Docker build context; the labelled twins and their planned drift check are gone (2026-10-19, revises 2026-08-26); the site and the desktop app share `web/layers/common` since #1732 — #1226, #947, #966, #1623, #1732, #1684, #1625
- SSR calls to the site's own `/api` forward the visitor, and a failure is never cached as an answer (2026-09-14) — #1557, #1546
- A public section card is its title and its content — no explanatory subtitle, no method footnote; definitions go in a hover (2026-09-25) — #1699
- Static game data is cached by the browser for the hour the server caches it (2026-09-15) — #1584
- A patch Data Dragon has not published yet falls back to its newest version; a published one stays pinned (2026-09-23) — #1693
- Focus moves to the content only when the path changes; the shell owns the single `<main>` (2026-09-17) — #1616, #1615
- Render-time behaviour is tested inside the Nuxt runtime, in a vitest project of its own (2026-09-17) — #1620
- Backend calls go through `useApi` / `useApiFetch`, not a bare `$fetch('/api/…')` (2026-09-17) — #1619, #1557
- The three text pages are cached at runtime (`swr`), never prerendered — a prerendered page freezes the build's runtime config (2026-09-18) — #1617
- A page awaits its API data in setup; a loading bar under the header covers the wait, only statics keep a skeleton (2026-09-23) — #1689
- Starred champions live under their own key next to the untouched player list, not in a v2 of it; matchups are not starrable; a clip's favourite stays the library's flag (2026-10-20) — #2080

## Design system — [`decisions/design-system.md`](decisions/design-system.md)

- A failed icon is hollow; a loading one is solid and moving (2026-09-02) — #1396
- An icon whose source never arrives is hollow too, never a raw Riot id — #1665, #1396
- The activity grid answers presence, not win rate: one rose-gold ramp keyed on games played (2026-09-03) — #1452, #1096, #927

- The rose-gold-only surface rule is reversed: neutral surfaces, a scarce accent, and a data axis of its own (2026-08-10) — #1060, #1059, #927
- Measurements are rose gold again: the cold→warm data axis is withdrawn (2026-08-11) — #1096, #1060, #927
- Measurements are set in Inter again: the mono stat face is withdrawn — #1060, #1111
- Long-form text uses Nuxt UI's prose layer, themed to the site's scale; non-global components, +3.6 KB gzip CSS accepted (2026-09-17) — #1624
- Page transitions are a fade-in of the content only, run by Vue's `<Transition>` once the destination has resolved; the old page leaves at once, never `out-in`; none on query-only navigations or reduced motion (2026-09-18, 2026-09-23, 2026-09-27) — #1621, #1689, #1714
- One error vocabulary: `UError` for a dead route, `FetchErrorAlert` for a dead region, a toast only for an action — never two surfaces for one failure; a failed refetch keeps the previous content, one toast + a stale notice (2026-09-22, #1668 2026-10-05) — #1661, #1668, #1234
- Empty states go through `UEmpty`, themed like the cards; an empty state is not an error, and "player not found" stays one (2026-09-22) — #1669, #1681, #1661, #862
- Keycap edge on surfaces and filled buttons; translucency returns for the floating header only; type stays Inter set tight with rose eyebrows, primary buttons stay rose gold (2026-09-27) — #1709, #1060
- Nuxt UI feeds Tailwind only the themes of the components the site uses (`componentDetection`): entry CSS 38.9 → 29.5 KB gzip, no visual change (2026-10-04) — #1641
- Inter and Geist Mono are committed (variable WOFF2, every subset) and resolved by a repo-local @nuxt/fonts provider; no build fetches a font, share cards use committed static Inter (2026-10-06) — #1106, #1335
- The UI refresh: `surface` becomes the dashboard tile's translucent pane (its blur an opt-in, `surface-blur`, since 2026-10-19 — #2129), stars under the home page only, Nuxt UI only and no accent bars, splash banners on detail pages, one profile page and one match detail for both apps (2026-10-07) — #1983, #1060, #1498, #1682
- One champion banner (`ChampionHero`) on the champion page and the app's champion page; one tier mark (`TierBadge`) on every surface (2026-10-13) — #2026
- The player-scoped champion page is the profile's page (`PagePlayerProfile`) on the champion's splash, no longer the champion banner (2026-10-13, revises #2026) — #2043
- One rank emblem (`RankEmblem`) on every row, one rank label in title case (`formatRank`: `Diamond II · 42 LP`); the truemains table and the champion page's rows share their columns (2026-10-13) — #2025, #2028
- A qualifier beside a figure or a title (thin sample, retired count, standard-build fallback) is a small warning-coloured dot (`WarningDot`) whose tooltip states the fact only, never a warning triangle (2026-10-13)

## Aggregates, retention and the schema — [`decisions/data-aggregation.md`](decisions/data-aggregation.md)

- Champion aggregates are replace-by-scope on *live* patches only. Old patches are frozen and must never be wiped — #466, #606, #694
- "Live" is the retained-patch window, not "has at least one match left"; one snapshot per run feeds both the cleanup set and the source rows — #1549
- Aggregate retention is opt-in per environment: `AggregateRetainedPatchCount` defaults to 0 (frozen forever); preprod sets 2 — #711; superseded for production by the two-level retention below once it ships
- Build aggregates live for the current and previous patch; every patch keeps a light win/pick/ban record per champion × position × elo bracket, written before the full aggregates are dropped (2026-10-06) — #1467
- The light record is `champion_patch_stats` (games, wins), folded with every scope replacement, plus the ban pair, which moves to the light level and leaves retention (2026-10-21) — #1956
- "Trend by patch" (now with an optional `eloBracket`) and the lifetime games total read the light record; the leaderboard's and personas' account-level cross-patch reads cannot and are #1958's to settle (2026-10-21) — #1957
- Timeline snapshots keep the canonical marks {5, 10, 15, 20, 30} only; retention prunes the legacy per-minute grid — #772, #694, #1599
- Aggregation is incremental per match, flagged on `matches`, never a full recompute — #811, #922, #920
- The pattern aggregation is incremental by slice, not fold-once: a rewritten participant sends its match back, and the bracket is the participant's stamp (2026-10-19) — #1367, #1365
- The operator champion game counts are a whole-table recompute of the raw rows in one statement, not a fold (2026-10-19, 2026-10-21) — #2132, #2217, #811
- Ban rate is its own aggregate pair with a stored denominator, and `ALL` is a stored band — not a summed one — #920
- Champion profiles (`champion_profile_stats`) are measured from the champion's own games over the full pool, never labelled by hand; the ranged flag is the one static attribute — #1449
- Damage profiles are served through the fold's own snapshot rule; the per-build split is a sibling table filled forward (no backfill); an unprofiled champion gets a Data Dragon class, never a share (2026-10-05) — #1905
- An item-context verdict is about an *edge* of the build tree: rates are branch-relative, a step with no real alternative is `Core`, and the gold-lead confounder dies with the flat grain — #1496, #1450
- A situation may only explain an item it could mechanically answer: the item-context whitelist is derived from the item's own categories, and three floors (bucket games, absolute lift, significance) decide a finding — #1450
- Item-context verdicts are derived and rebuilt per run while the counters stay additive per match, so the API read carries no statistics — #1450
- A thin item-context bucket widens backwards through patches, both ends together, and records the window it used — #1450
- The item context carries no elo dimension: splitting by rank would starve the buckets the feature rests on — #1450
- Known gap: an item-context axis does not hold the lane opponent out, because that needs an opponent dimension ~70x the counters — #1450, #1462
- The next-item model is a naive-Bayes sum of shrunk log-ratios over the item-context counters, derived beside the verdicts; it predicts the mains' choice, never a win-rate argmax (2026-10-01) — #1749
- Measured on held-out games before shipping: composition moves boots (right 400 vs 215 where it overrides the base order), barely legendaries (534 vs 509) — enemy builds are the lever; correlated axes are summed, grouping measured no better (2026-10-01) — #1749, #1750
- The next item reacts to what the enemies have built: seven in-game axes replayed from their timelines to the parent item's completion (completed items only, Data Dragon armour/MR, root branch none, provisional bands with measured quantiles in each run's summary) (2026-10-21) — #1750
- The in-game axes of what the enemies built are overlay-only: folded and read by the next item, never a site verdict, so the hover cards carry no copy for them (2026-10-05) — #1750
- The fold loads the enemies' timelines one query per side, untracked participants only; `MatchBatchSize` stays at 300 until a preprod re-fold measures it (2026-10-21) — #1750
- No pick+ban "presence" figure, despite it being standard elsewhere — #920
- A dimension's identity is enforced by the schema (canonical UNIQUE index, CHECK, generated key), not repaired afterwards (2026-09-03) — #1418, #911
- A champion stat row's invariants are write-time CHECKs added `NOT VALID` — `Wins <= Games` on every stat table by convention, `LaneGames <= Games`, canonical lanes, a real opponent; the one lane sentinel is pinned to `''`, not made `NULL` (2026-10-06) — #1365
- Rank snapshots are capped at one row per account per UTC day (DB-level unique index) — #907
- `(GameName, TagLine, PlatformId)` on `riot_accounts` is a plain, NON-unique index. PUUID is the only real identity — #901, #902
- PUUID indexing is intentional — do not propose dropping it or migrating to `RiotAccountId`-only — #123, #124
- `ItemEvents`/`SkillEvents` use LZ4 TOAST compression per column; old rows are rewritten by the #1946 repack, not a migration (2026-10-06) — #123
- Pattern aggregates use a junction model (`champion_aggregate_patterns` + globally deduplicated `champion_dim_*`)
- The patch is a column on `matches`, not a `LIKE` prefix over `GameVersion` (2026-09-02) — #1368, #589, #598
- A final inventory is slots 0–5 plus Riot's role-bound slot (a bot laner's boots always live there); the trinket slot is never a build input; legacy rows are backfilled from the item timeline, not re-fetched (2026-09-17) — #1612, #1607
- The pace benchmark is a per-minute CS / gold-earned histogram per (patch, tier, position), folded from the timeline in memory at ingestion, once per match, the lobby at its tracked account's tier — never a per-minute grid (2026-10-05) — #1912
- The form benchmark (KDA, KP, CS/min, deaths, vision/min) is a whole-game histogram in its own table, folded in the pace benchmark's pass under its claim, so both describe the same games; no back-fill (2026-10-20) — #2076

## Backend code conventions — [`decisions/backend-conventions.md`](decisions/backend-conventions.md)

- Where API reads live — #865, #924
- Tables are snake_case, columns are PascalCase — and enums split by who reads them (2026-08-28) — #1251
- Postgres columns stay quoted PascalCase; only tables are snake_case — #227
- The EF compiled model must be regenerated on every schema change — #242
- Configuration defaults live in the class, and the two champion games floors are two keys — #1034, #860, #889
- A unit of work covers the writes and nothing else (2026-08-28) — #264, #1229
- `backend/Api` has a stated layout: controllers by resource, services by feature (2026-09-07) — #1520, #1451
- A child table with a natural key uses it as a composite primary key — no surrogate `Guid` beside it (2026-09-25) — #1697, #541, #124
- Ingestor options: bounds are attributes checked by a generated validator, cross-field rules stay lambdas (2026-10-04) — #271
- A role is a string from `LanePositions` (no enum, `MID`/`BOT` accepted on input); timeline marks live once in `Data` (2026-10-04) — #1232
- Integration test classes reset their stores in `InitializeAsync`; migration replays get a scratch database; clocks are frozen through the factory (2026-10-05) — #1246
- Test methods are named `MethodName_State_ExpectedBehavior` (PascalCase segments, never snake_case), enforced by an `IDE1006` rule on `backend/tests` (2026-10-06) — #1246, #290
- Threading + Roslynator analyzers run on every project: async-correctness rules are errors, JTF-only and false-positive rules are off, Roslynator style stays IDE hints (2026-10-05) — #294
- A process's own timestamp on `riot_accounts` is stamped set-based, never through a tracked entity (2026-10-13) — #2046, #231

## Ingestion pipeline — Riot budget, pacing and intake sizing — [`decisions/pipeline-riot-budget.md`](decisions/pipeline-riot-budget.md)

- Riot calls are paced by a limiter keyed on the routing value, because that is the grain Riot enforces — #1359, #855
- The pipeline runs as two lanes — Riot-bound and Postgres-bound — because they have opposite bottlenecks, in both environments — #1362, #1490, #1360
- The lane is derived from the process name; the mode it ran under is recorded (2026-09-02) — #1362
- Match ingestion fans out one worker per platform, and stays sequential inside one — #1359
- Match fetching runs as permanent per-region workers beside a 1-minute maintenance pass, held to a target rate (default 30 calls/min per regional host, prod's prior average) beside reserved shares for AccountRefresh/ManualSeed (2026-10-21) — #1457, #1460
- Throughput is raised in held stages (30 → 36 → 40 → 44 calls/min per regional host), behind a memory and free-disk brake that stops claims — never a batch in flight — with an ops event per activation and release; CPU out of scope (2026-10-21) — #1459, #1460
- A Riot call that stores nothing is a bug, not a cost (2026-09-02) — #1358, #1357, #1312
- Every outbound client asks for compressed responses (2026-09-16) — #1601
- Riot quota is read per routing host, from route-keyed rollups only, kept 30 days (2026-10-05) — #1458, #1457, #1460
- A per-run budget is bounded by a cadence, or the daily cost is whatever the loop speed makes it (2026-09-04) — #1474, #1460, #1313, #900
- Match participation is the primary activity signal for mains; mastery answers for the unseen tail (2026-10-04) — #1475, #900, #1474
- The claim's breadth class is accounts never ingested; their leftover candidates are neither promoted nor kept queued (2026-10-06) — #1535
- The claim's established-main share is per platform, from each platform's own coverage deficit (2026-10-04) — #1533
- The coverage floor is 50 mains per champion per region, and the claim's split is centred on it (2026-09-08) — #1531, #1361, #1150, #900
- The intake is sized by the claim, not by the ladder (2026-09-02) — #495, #900, #1150
- Region balance is a target, not a quota: coverage deficit allocates every budget (2026-08-19) — #1149, #495, #900
- Iron → Platinum are sampled into the pace benchmark by `PaceSampling` under a hard per-day call cap (every call counted), the games never stored — only a ledger of ids and the bins (2026-10-05) — #1912

## Ingestion pipeline — processes, leases and resilience — [`decisions/pipeline-ingestion.md`](decisions/pipeline-ingestion.md)

- A starter-item scan must only trust events that prove ownership, not every event mentioning an item — #923
- The ingestor Worker isolates failures per process — #443
- A streamed Riot response can fail *after* the resilience handler has waved it through — isolate the call site — #253, #1052
- A CommunityDragon patch branch that does not exist yet is a transient condition, not a fatal one — #1107
- Ranks are read from the ladder, not from one account at a time (2026-08-30) — #788, #1312, #1149
- A lease is only kept if something reaps it (2026-09-01) — #1344
- A redeploy is an outcome the pipeline records, not an accident it absorbs (2026-09-07) — #1513
- Jungle first-clear tracking was built, then removed entirely (2026-08-24) — #1186, #1195, #535
- A shell match (`endOfGameResult` ≠ `GameComplete`) is refused at ingest; a remake is Riot's flag *or* the 5-minute floor (2026-10-06) — #1364, #1365

## Performance, caching and incidents — [`decisions/performance-and-incidents.md`](decisions/performance-and-incidents.md)

- The live draft reads never touch the table's heap: both `match_participants` indexes are covering — #1663, #1659

- An expensive read path behind a TTL cache needs a single-flight, not a lock — #870
- Postgres runs with `max_parallel_workers_per_gather=0` in every compose file — do not re-enable — #589
- The API retries transient Npgsql failures (not client timeouts); the Ingestor does not (2026-10-06) — #1634
- Consequence: every heavy aggregate runs single-threaded, so batch work must be chunked — #603, #594, #632
- Aggregation is chunked per champion to bound memory — #600
- A heavy `CREATE INDEX CONCURRENTLY` must never be a startup migration — #595, #597, #598
- Npgsql pools are capped per service against Postgres `max_connections=100`, each sized to its PgBouncer share since #1632, and a PgBouncer wait is bounded at 30 s — #437, #461, #462, #1570
- Server-side timeouts: API `statement_timeout` 60 s and pooled `idle_in_transaction_session_timeout` 60 s through a dedicated PgBouncer database, migrations `lock_timeout` 5 s with retry (2026-10-06) — #1631
- Postgres ships tuned settings in compose, and parallelism stays off (2026-09-02) — #1366, #589
- Champion reads are cached until the data changes, not for 60 seconds (2026-09-02) — #1374, #1368
- A leaderboard miss is computed once, and the champion page never asks for it during SSR (2026-09-15) — #1570
- A Riot ID resolves through a functional index on the lowered name and tag (2026-09-15) — #1570
- The public web server runs one Node worker per useful core (2026-09-15) — #1579
- The payload is inlined on first load and extracted for client navigation only (`payloadExtraction: 'client'`; it only reaches the `swr` text pages) (2026-10-04) — #1618
- Paginated reads stay on offset paging; keyset waits for a preprod `EXPLAIN` showing a real cost at a page actually reached (2026-10-06) — #1635
- No ops read counts `match_participants` on request: champion stats read a rollup, the cockpit's corpus totals are planner estimates that degrade alone, and a failed cold pass backs off (2026-10-19) — #2132, #1427
- The scaling chart reads a per-match fold (`champion_scaling_stats`), proven equal to the live SQL it replaced; the uncalled `item-timings` endpoint is gone rather than pre-aggregated (2026-10-21) — #1368, #628

## Infrastructure and deploy — [`decisions/infrastructure-and-deploy.md`](decisions/infrastructure-and-deploy.md)
- The rate-limit partition is the visitor (last `X-Forwarded-For` hop, trusted proxies only), not the connection — this reverses "100 req/min per IP" — #1546
- The admin portal is a standalone Nuxt app with its own deployment and domain — not a `/admin` route — #96, #91, #376
- Preprod auto-deploys from `compose.preprod.yaml` on every push to `develop`; prod auto-deploys from `compose.prod.yaml` only when a GitHub Release is published — #717, #751
- Images publish an immutable `:<sha>`/`:<version>` tag alongside the moving `:preprod`/`:latest`, and compose references the immutable one — #738, #765, #767
- Prod deploys from the version-controlled compose file — no hand-maintained host compose — #462
- Preprod and prod both apply migrations out-of-band, as a discrete CI step before the images roll — not at startup — #208, #246, #1058
- An incomplete prod deployment configuration fails the release run; it is never a green skip — #1228
- The deploy preflight rejects `INGESTOR_JOB_MODE=Full` when the compose file runs an aggregate lane — #1493
- A deploy job proves the environment moved; the API acknowledgement is not evidence — #1394, #1365, #1374
- The deploy pre-pulls the images over the CI key (forced command `migrate` | `pull <tag>`) before Docker Manager recreates anything (2026-10-05) — #1089
- Both deploy pipelines serialise at workflow level, not per job — #1228
- Integration tests run on pushes to `develop`/`master`, not only on pull requests — #1228
- Preprod tracks `develop`, has its own Riot API key, and is deliberately tiny — a new key forces an empty database — #705
- Preprod runs prod's parameters at a smaller volume, behind its own plain-HTTP edge Caddy (2026-09-14) — #1558, #1546
- Preprod builds carry a prerelease version, tagged only after they deploy (2026-08-24)
- Caddy terminates TLS and is the only public entry point in prod — #433, #430, #426
- The admin `/analytics` iframe stays on Umami's public share view, not the authenticated app — kept as-is on purpose, 2026-08-04 — #1013, #1014
- Umami session replay/heatmap rows are purged after 7 days by a sidecar container, not left to grow — #680, #1018
- Analytics stay inside the CNIL consent exemption: no session replay, an opt-out on `/privacy` — no consent banner (2026-10-18); since 2026-10-19 a random anonymous visitor id, 13 months from creation and never extended, counts returning visitors across months (data retention: #2092) — #2089, #2149
- `/ops/*` is the only authenticated API surface
- The Riot API key is a permanent *personal* key — not a 24 h dev key, and not production-approved — #532, #780
- Preprod runs at test volume on its shared host, not at load-test size (2026-09-16)
- The edge Caddy sets the security response headers the app frameworks did not (HSTS, CSP per vhost, X-Frame-Options, nosniff, Referrer/Permissions-Policy); preprod carries the non-CSP subset — 2026-09-24
- The API port is published on loopback only (not `0.0.0.0`), and pgAdmin was removed from every stack — 2026-09-24
- Prod's edge Caddyfile is inline in `compose.prod.yaml` (the deploy writes only the compose file and `.env`), and the release asserts compression and HSTS/CSP on the live site — #1598
- Each service gets a fixed share of PgBouncer's 30 server connections — API 22, ingestors 8 (fetch 5 + aggregate 3), no reserve — and its Npgsql pool matches it (2026-10-19) — #1632
- The edge Caddy is its own Docker Manager project, redeployed only when it changes, and answers a `503` maintenance page while the app stack rolls (2026-10-20) — #2179

## Admin portal — observability data — [`decisions/admin-observability.md`](decisions/admin-observability.md)

- Admin-portal data lives in Mongo, not Postgres (2026-08-01) — #416, #93, #925
- The disk figure sums Postgres and Mongo, because there is one disk — #1023
- A step change in what is measured is not growth, so the forecast restarts at it — #1023
- Ingestion throughput is measured from the run summaries, not from `matches.CreatedAtUtc` — #1025, #982, #988
- The counters are summed in memory because the summary is stored as opaque JSON text — #1025, #990
- The candidate funnel measures Validated and Demoted — its only exit and only negative outcome — #1024, #1029
- A forward-only counter renders as absent, not as zero, and key presence is what says which — #1024, #924
- `ValidatedAtUtc` had never been written in production, and the queue-latency snapshot is why that surfaced — #1024
- Queue latency is a snapshot over retained rows and is labelled as one, rather than being faked into a series — #1024
- The candidate stock is snapshotted hourly, because it cannot be reconstructed afterwards — #1403
- A recorded zero is a measurement; an unmeasured period is absent — #1403, #924
- A stock is sampled across time and summed across platforms — never the other way round — #1403
- The candidate level is charted in rows or accounts, with the unit stated; accounts are snapshotted, not derived at read time — #1534
- Daily storage snapshots go to Mongo and are keyed on the day, not the run — #925
- The disk forecast is absent rather than approximate when the data can't support it — #680, #925
- Logs and metrics live in MongoDB, not Postgres, with two different guarantees — #416
- Ops logs are signal-only: Polly retry noise is not persisted, domain events are — #444, #93
- Riot API caller attribution uses an `AsyncLocal` ambient context, mirroring `IterationContext` — #1035
- The budget-headroom estimate (#1035) requires 24h of rollup history before it will extrapolate, and picks the app rate-limit window with the smallest daily ceiling as "binding"; it is computed per routing host and the tightest host binds — #1894
- The configuration viewer is an allow-list, and each host reports itself (2026-08-08) — #1034, #1033, #924
- The pipeline chain is drawn per lane, not as one flat list (2026-09-02) — #1399, #1362
- An admin number is either actionable or it is not printed: primary-lane below-floor lines, no `Ext. samples` column (2026-09-03) — #1442
- Request failures reach the ops logs as counted signal, not as request logging: 429s rolled up per visitor, 5xx and aborts with their request, channel drops counted (2026-09-14) — #1555, #444
- The frontends report their server errors through the API, with a key of their own (2026-09-15) — #1556, #1555
- A request the client abandoned is reported by the frontend that saw it, and cancels its API call (2026-09-15) — #1569
- The Ingestor's own meter is folded into Mongo per minute and shown on the Riot API tab — no OpenTelemetry pipeline, no tracing (2026-10-05) — #1636
- The ops scripts kept outside the repo report their runs into `process_runs` through allow-listed ops endpoints, and the Processes page shows them on a *Scrapers* tab (2026-10-19) — #2119
- Iterations are paged from a header per pass (`process_iterations`), not grouped from the runs (2026-10-19) — #2124

## Admin portal — health panels, charts and vocabulary — [`decisions/admin-health-and-charts.md`](decisions/admin-health-and-charts.md)

- A health panel may not pass what it did not measure — #924
- A detector shares the constraint's definition of the bug it audits — #924, #911, #1418
- Detector thresholds are configuration, not constants
- A health panel answers before it reports — #992
- The health cockpit (`/health`, #1031) holds no depth of its own — every tile is a link, and the verdict is judged server-side
- A process that has never recorded a run is `unknown`, not amber; an abandoned run is a warning, not an error — #1031
- A chart's mark is chosen by what the series measures: flows are bars, stocks are lines (2026-08-25) — #924
- Slots 4-6 of the chart palette are a measured downgrade, spent on one chart — #1403, #1404
- Admin bar charts go through a wrapper, because vue-chrts' bar tooltip is broken three times (2026-08-25, extended 2026-09-03) — #1218, #1404
- The portal's charts are on the public site's design system, while its chrome is not — reversed 6 days later, see next line (2026-09-03) — #1404, #1059
- The portal's chrome moves onto rosegold/ink too, reversing the #1059 scoping (2026-09-03) — #1409
- The portal navigates by question, not by table: four sidebar groups, one Accounts hub, Riot API as a Processes tab (2026-09-03) — #1410, #1416
- A panel answers in one line; the explanation lives behind an info control (2026-09-03) — #1414, #1416
- Logs opens on Warning and above (an explicit `?level=` still wins), and pages are reachable by name through a ⌘K palette rather than a longer sidebar (2026-09-03) — #1415, #1416
- The admin portal has one status vocabulary and one duration ladder (2026-08-28) — #924, #1024
- The admin's tracked-region list stays a checked-in constant, not a read of `/ops/configuration` (2026-08-28) — #1249
- The cockpit's region balance is informational, and its coverage deficit is the claim allocator's own arithmetic over the same counts (2026-10-04) — #1153, #1150

## Desktop companion — [`decisions/desktop.md`](decisions/desktop.md)

- The app follows the reference client's layout — a sidebar to every site section; the gameflow phase still opens the draft — #1671
- No win probability: the draft strip carries the clock, the middle the lane duel (lane win rate only) — #1671
- One selected card on the draft board, ringed in the primary colour: the player's by default, any placed champion on click (build + lane duel); its lane opponent faintly ringed (2026-09-28) — #1671
- The draft shows each team's damage mix (weighted like `DraftAxisEvaluator`, no figure with two unmeasured picks), a non-scoring chip on a pick that answers a one-sided team, and the boots / first item the draft clearly moves (next-item read at draft time, ≥5 pts and ×1.25, enemy/lane reasons only); runes not yet (2026-10-05) — #1907
- Champion select is the live draft only; a draft played by hand is a dev tool (`/dev/draft-sim`) feeding the shell the client's payloads (2026-09-28) — #1671
- An enemy's lane is corrected from its lane icon (a menu of lanes); guessed lanes carry no "?"; build header = icon, name, lane icon (2026-09-29) — #1671
- No browser chrome in the app: no back/forward arrows, no patch label (2026-09-28) — #1671
- The window is resizable down to 960 × 640, keeps its size (points) and place (pixels) in the shell's own file, a place on an unplugged monitor reopening centred on the primary; below 1100 px the sidebar is a rail of icons (2026-10-05) — #1914
- Nothing pops up by itself during champion select or a game: the update toast waits for the phase to end (sidebar only meanwhile); badges read as plain words (2026-10-05) — #1914
- Lists are tables with fixed columns under headers (truemains, champions); the tier list is the site's tier cards (2026-09-30) — #1719; superseded for every page the app shares with the site (#1732)
- The home page announces the app right under the hero, above "This patch"; `/download` shows a real capture of the app (live API data, never a fixture podium) over plain typography, no icon-tile cards; the home banner shows the dashboard whole, on fixture data by the product owner's exception (2026-10-01) — #1725
- `/download` is organised by moment of a game (champion select, in game, recordings), each with the app's UI drawn live in HTML and no caption under it; the overlay and the recap sit on a real frame of one of our own games, no table of contents (2026-10-16) — #2072
- The beta ships unsigned as `desktop-v*` GitHub pre-releases, resolved by the site (download + update feed), and updates itself on the player's click (2026-09-28, revised 2026-10-01) — #1719
- App version bumped by hand; each bump merged to develop builds a pre-release preprod serves; production serves only a build promoted by hand (`Desktop promote`) (2026-10-01; revised by #1799) — #1772
- Every app change merged to develop builds a `X.Y.Z-beta.N` preprod build; a bump's production build goes out by itself once the site release in production contains it or matches it on `web/` + `backend/{Api,Core,Data}` (2026-10-02) — #1799
- Each app version is built twice — production flavour (`truemain.*`) and preprod flavour (`truemain-<version>.*`, *TrueMain Beta*) — and each site serves, and updates, only the build that reads it (2026-10-01) — #1779
- The app polls its own site's feed every 15 min, downloads in the background, installs itself at launch outside champion select/game and otherwise offers "Restart now"; "Check for Updates…" in the macOS app menu / a Windows tray menu (2026-10-01) — #1789
- Picks are ranked from the player's mastery pool or the whole lane, never a "meta" slice; comfort (mastery, recent games) chooses the pool and is a shown reason, never a score term; enemy-team component is #1713 — #1675, #1906
- A draft suggestion card leads with its strongest reason's measured figure, never the ranking score; reasons are worded in one util — #1906
- The draft requests carry the player's Solo/Duo tier and above as their bracket, every bracket when unknown; no patch, the server's (2026-10-21) — #1906
- Bans are suggested on our ban turn and, in ranked planning, once we have hovered a pick (which they then protect) — same strip, a `mode` prop — #1906
- The app draws the site's components as labelled twin copies, with app behaviour in shims beside them; superseded page by page by the shared layer — #1687, #1732
- The pages the app shares with the site (champions, tier list, matchup, truemains, favorites) are one implementation in `web/layers/common`, which both apps extend (2026-10-01) — #1732
- A tab changes on click (the app's own `<Suspense>` over the shared page's await, header + skeleton); a bar across the window's top edge runs on every page load, not only navigations — the site keeps #1689 (2026-10-01) — #1788
- Installing the app never feeds ingestion: dashboard and pool come from the local client for any account; a tracked main gets a link to their site page, an untracked player no prompt (2026-10-06) — #1682, #1942
- Dashboard roles come from the participant slot on role-assigning queues, never the client's lane guess; a game's build and skill orders from TrueMain's copy, left out when only the client has the game (2026-10-01) — #1768
- The build pane shows the site's core without its build path (the tree draws it), runes beside; build rows are icons only; a true main's click shows their own build (2026-09-28) — #1671
- In champion select a true main's build is the draft build read from their games alone (`player` on `composition-build`), their usual build when they have no game of it (2026-10-08) — #1987
- Icons bundled at build time, hash routing, images drawn without a `load` gate (WKWebView); opaque pictures over an `img-skeleton` background, transparent ones bare (2026-10-06) — #1949
- In-game overlay: six panels in their own windows, none before `GameStart` (the loading screen board lives in the app only since 2026-10-04: form on the champion + last ten games as win/loss bars, via the client, names by puuid, #1753, #1803; a Streamer Mode player stays anonymous, nothing read about them; a true main of their champion carries the TrueMain mark alone, figures in its tooltip, one batch lookup per game, 2026-10-05, #1910) — build path (the next item, gold still needed or "Can buy now", its components in the mains' order since #2183), win probability (always), your pace (CS/min + curve, gold/min; no damage/min — not in the API), item value (TAB held by default; each panel always, chord held or toggled since #1915), objectives (next spawns of the dragon pit, grubs, Herald, Baron, from the season's rules and the feed's kills, #2078; one compact strip of pills top centre with the soul race under it, waiting pills in grey, constant height, the item value moved to centre right, #2184); over the game process only, above the captured display, never key, click-through; TAB and the shortcut read from the keyboard's state (no Input Monitoring); macOS and Windows (2026-10-02) — #1673, #1795, #1752
- Lane gold sits on the game's TAB scoreboard rows (reverses #1752's own-panel line): a see-through, click-through, never-dragged window placed from the game window and the client's HUD scale (measured constants, to be confirmed in game; a ±60 px nudge), blue side left / red right in lane order, mirroring moves nothing; TAB held shared with the item value, which drops its lane rows while the plates are on; transparent webview via `macOSPrivateApi` (2026-10-21) — #2186
- The overlay on Windows: the same panels through one platform-neutral driver, each a topmost non-activating layered window, over Borderless/Windowed only (Windows draws nothing over exclusive Full Screen), Alt+Shift+O (2026-10-02) — #1798
- The overlay goes on the monitor holding the game's window (read while it is frontmost, no permission, no handle on the game), the primary one before any game (2026-10-05) — #1914
- The overlay is set up on its own sidebar page, out of game: a copy of the screen to drag panels on, × to hide one, a column of hidden panels to drag back (2026-10-03) — #1819
- Overlay panel triggers: each panel always / while a chord is held / toggled (resets each game); chords by physical key, read from the keyboard's state (never hooked), so the game gets the keys too — warned against the player's League binds, never refused for it; reserved, system and duplicate chords refused (2026-10-05) — #1915
- Win probability in the overlay: each lane's CS/level/kill lead weighted by a regression fitted on our ranked games (no item gold; the support weighs little), plus the map (turrets, inhibitors down, drakes/soul, Baron, Elder) with hand-set weights — the product owner's call, reversing #1671's line for in game; the draft keeps none (2026-10-02, refitted 2026-10-04) — #1795, #1864
- After the game, the same model (now in `web/layers/common`) draws the whole game's curve and its five largest turning points, built on the client from the client's timeline in the recap and the dashboard, never from TrueMain's copy (2026-10-05, desktop-only since 2026-10-12) — #1911, #2009
- Runes are imported on a click only, from a discreet corner icon; the app reuses its one `TrueMain: ` page and never deletes a page of the player's; no spell import (2026-10-03) — #1678
- Items are imported on a click too, as one `TrueMain: ` item set replaced in place; the whole-list write round-trips the player's sets raw and writes nothing after a failed read (2026-10-05) — #1908
- A kit is the player's own item tree + rune page + two spells per champion and position, kept on their machine; the active one takes champion select's build pane (its imports reuse the one TrueMain set and page, and set its spells on a click — revising #1678/#1909 for kits only) and drives the next item until it runs out (2026-10-21) — #2185; its boots are a list to pick between, its tree legendaries only — #2201; its picker filters by the shop's classes, served by the API from the game data — #2203; it is offered on several positions, listed on a My kits page, and every kit is a choice in champion select beside the draft's build, no active switch (revises #2185) — #2205
- The app hovers, locks and bans in champion select on the player's click only — lock = complete what the client shows hovered, from one strip button; a ban (or an ally's intent) asks first; no auto-lock/ban/accept, ever (2026-10-05) — #1909
- The player's own account is never highlighted — loading screen, game page, match rows, scoreboard, runes; ours only orders the sides (2026-10-02); exception: our line on the match detail's gold / XP / CS curves, in the primary colour (2026-10-19) — #1803, #2102
- The match detail's Timeline view opens on the ten players' gold / XP / CS curves (lead band, kills and objectives as the recap's blue-above / red-below dots on the axis, a pinning list per side) above the win probability, from the client's timeline, app only, shown only when the timeline carries gold (2026-10-19) — #2102
- The dashboard's Today card: games, W–L, time played and net LP since local midnight, LP summed only over measured games (partial sum flagged), following the queue filter, absent on a day without a game (2026-10-16) — #2073
- The dashboard's latest clips: a small card (≤ a quarter of the page) under the match history, four clips in a row with empty slots keeping its shape, opening the Recordings page's own clip player; absent with no clip; a recorded row's play button is the icon alone in a slot every row keeps (2026-10-18, moved 2026-10-20) — #2077, #2074
- The dashboard has no goals: the #1913 goals card and its local store were removed at the product owner's request (2026-10-20) — #1913, #2074
- The app's footprint is measured, never estimated: whole process tree (WebKit's services by responsible process, WebView2 by parent), physical footprint / private working set, median + p95 per scenario; `/download` shows a figure only once measured, a competitor's only with a published source; an overlay panel has a webview only while it can show (2026-10-05) — #1916
- Desktop usage is measured by the app itself (anonymous install id, counters folded per install and UTC day in Mongo, 13-month TTL, opt-out in the native menu), downloads by the site's redirect; nothing read from the League client is sent (2026-10-02) — #1805
- Game recording: two quality choices (resolution, 30/60 fps), everything else derived; highlights from the timeline, live feed as fallback; disk budget drops the oldest unpinned; unsigned beta and a GPL capture library both accepted (2026-10-01) — #1744, #1754
- Screen capture runs in a native helper process per platform (Swift + ScreenCaptureKit + VideoToolbox on macOS), driven over JSON lines; encoder settings still computed in Rust; shipped inside the app bundle, also cutting clips and taking thumbnails (2026-10-01) — #1745, #1744
- macOS records the game's display filtered to the game's windows (a full-screen game's window sends only idle frames); Windows keeps the window, its display is the whole monitor (2026-10-03) — #1826
- Recordings are served by the app's own `recording` scheme (explicit ranges answered in full, recordings folder only), not Tauri's asset protocol, whose 1000 KiB range cap blacked out games past ~36 min (2026-10-04) — #1830
- The Windows helper is Rust (Windows.Graphics.Capture + D3D11 video processor + Media Foundation hardware encoders, WASAPI process loopback for the game's sound only), same protocol, added to the installer as a sidecar at release time only (2026-10-02) — #1797
- The app records only against a real client, into the system's videos folder; objectives from the match timeline only; Screen Recording asked from the Recordings page (2026-10-01) — #1744
- Clips are cut by hand from the post-game recap in v1 (reverses #1744's "no trimming by hand"); saved clips are never pruned by the budget; the recap opens on its own only over the game page or the dashboard; Recordings page in DPM's layout (2026-10-01) — #1755, #1777
- The mark key (⌥⇧0 by default, read like an overlay chord, only while a game records) notes the video's second; each mark becomes a saved clip of the 30 s before it, cut once the game ends, adjustable in the recap (an adjusted range replaces the clip); it is #1767's instant replay too: 30 s / 1 min / 2 min kept per mark, a 2.5 s confirmation over the game in an overlay window of its own, titled "Ahri at 12:34"; the replay buffer without the full game stays deferred (2026-10-20, 2026-10-21) — #2079, #1767
- Automatic clips: once a game is finalised, the plays the player picks (multi-kills from a triple, a multi-kill into an objective, steals, aces, first blood, teamfights; all on by default) are cut as saved clips, titled only from the events (timeline + the live feed's Stolen/Ace/FirstBlood, sided by the player list, never guessed); overlapping windows merge (2026-10-21) — #1766
- A clip is published through YouTube's upload page in the player's browser (file dragged from the app, prepared text, link given back), never the YouTube Data API: shared per-project quota, sensitive-scope verification, forced private uploads (2026-10-12) — #1815
- A published clip may delete its video and play from YouTube's embed, framed through a page the shell serves on 127.0.0.1 (YouTube refuses `tauri://` pages, error 153); only a linked clip can drop its file; a link is kept only once oEmbed confirms an Unlisted/Public video titled as the clip (2026-10-12) — #2005
- The dashboard reads the player's record from their own client (history, ranked, profile skin, scoreboards, timelines), not TrueMain's API; the site's ranked card, champions and roles lists (shared components since #2029, dashboard figures in slots); the match row, its accordion (a compact same-surface match detail); LP history noted locally; form = last 5 vs own average, no composite score (2026-09-30) — #1683
- The dashboard's games carry the site's per-game performance score / MVP / ACE: TrueMain's when it holds the game, else graded on the client by a TS port of the model pinned to the C# by shared vectors, from the scoreboard and the timeline's canonical marks (2026-10-20, reverses #1683's "no score") — #2075
- The running game is read from its Live Client Data API every 2 s while `InProgress`, sent as a snapshot then numbered changes; `/game` opens from home or the draft only, shows the scoreboard's own information; enemy visibility still to verify live (2026-10-01) — #1748
- The Game page's player lines show standing, role fit (main ≥ ½ of recent role games, autofill < ¼), champion record, streak and recent games instead of items and K/D/A (reverses #1748's scoreboard copy); all via the client, plus one ranked request per player (2026-10-04) — #1828
- Each Game-page lane shows an arrow + chance towards the favoured side: TrueMain's champion head-to-head (shrunk to 50 %) moved by each player's last 10 games on the champion — win rate and gold/CS/XP leads at 15 vs their lane opponent, weighted per role; figures not shown (revises #1828's no-composite rule for lanes only) (2026-10-04) — #1863
- The next item sits over the game board: the mains' next legendary, one reason, the gold left and the components buyable now, two runners-up and the boots; asked on item changes only; our gold in 50-gold steps (2026-10-01); until a starter is bought, the composition build's starter basket above it and in its place on the overlay (2026-10-08) — #1751, #1989
- The next item also lays the build out as one row, the boots at the step the mains most often complete them at (a `BootsTiming` slot of the item-context fold, ≥ 20 games, no backfill: it fills from matches folded after the change); due boots become the main item (2026-10-19) — #2101
- The next item's components come in the order the mains buy them (`champion_item_component_orders`, folded in the item-context pass, mean rank, ≥ 20 games over up to three patches, else recipe order; no backfill); the gold walks that order and stops at the first it cannot cover, "≈" across a 50-gold step; the overlay panel shows it, labelled "Build path" (2026-10-21) — #2183
- In game the next item sends the five enemies' inventories (champion select sends none, which is not an empty one) and reads what they have built; its reason for those axes is the app's own game-state wording (2026-10-21) — #1750
- The overlay's pace sets CS/min and gold/min against the median of games at the player's Solo/Duo tier (every tier fetched, the tier picked locally); no Solo/Duo standing → no comparison; a reference, never advice (2026-10-05) — #1912
- The dashboard's form tiles carry the median of the player's own Solo/Duo tier (not the tier above) on their main role, in muted text with the tier's emblem; under the floor or without a standing, none; dashboard only (2026-10-20) — #2076

## Workflow conventions — [`decisions/workflow-conventions.md`](decisions/workflow-conventions.md)

- Language split
- `develop` is the default branch
- Branch names
- A PR is done
- CI traps — #1236
- API wire conventions
- Every issue goes on GitHub Project #2
- The frontends lint with ESLint (`@nuxt/eslint` + vue + a11y); already-broken rules and all a11y rules are warnings, the rest errors (2026-10-06) — #1440
- Code quality is measured by SonarQube Cloud from its own workflow; its built-in quality gate is required on `develop` through the workflow's `Quality gate` job; Roslyn diagnostics are not re-imported; Vue components count toward coverage (2026-10-13) — #2051, #2052
- Every PR workflow plans its jobs from the changed paths (Sonar coverage and backend tests, desktop jobs, image builds); the Claude review posts blocking points only (2026-10-21) — #2218
- Third-party GitHub Actions are pinned by commit SHA, never a tag (2026-10-13) — #2052
- CodeQL runs from a versioned workflow, per language and only on the paths a PR touches; full scans on `develop` and weekly; no Swift (2026-10-13) — #2057
- Load tests run against preprod from GitHub Actions, never from the preprod host (2026-09-14) — #1559
- Agent disciplines (debugging, grilling, glossary, retro) adapted from mattpocock/skills; review split into Spec and Standards axes; PR bodies carry a merge-danger call (2026-10-05) — #1938
- The repository is licensed under PolyForm Noncommercial 1.0.0: public and readable, no commercial reuse without a separate license (2026-10-16) — #2070

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
