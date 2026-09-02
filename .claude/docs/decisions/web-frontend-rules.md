# Web frontend rules (hydration, fetches, icons, tooltips)

Part of the [decision log](../decisions.md). Format: **Decision** — why — `source`.

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

The rule covers anything else an immediate watcher can trigger, not just fetches: `useErrorToast` registers
its watcher under `import.meta.client`. Its `if (!value) return` guard made the SSR run a no-op only because
every error ref wired to it happens to come from a `server: false` fetch — true, unwritten, and untrue the
day one is pointed at the server-rendered build summary or the leaderboard, where a toast pushed during SSR
serialises into the payload and pops up unprompted for every visitor served that render — #1234.

**A closed `enabled` gate resolves `success` with an empty model, so the gated composables expose their own
`pending`.** `createChampionPatchSlice`, `useChampionTrend` and `useChampionPatchDiff` hold their request
until the champion's lane lands, and while held they resolve the empty read-model — which reaches
`status: "success"` with nothing loaded. A consumer driving a skeleton off `status` therefore renders its
"no data" state for the whole (client-only) champion fetch and only then fills in. Each composable now
returns `pending = gate closed || isLoadingStatus(status)`, deliberately superseding Nuxt's own `pending`
(which only knows about the request), so the trap is composed away once instead of re-documented at every
call site — #1234.

**Every hand-rolled fetch composable carries a monotonic request token.** `useCompositionBuild`,
`useCompositionBuildGames`, `useTruemainSearch` and now `useTruemainFetch` (profile / rank history /
activity / matches) all drop a response whose token is no longer the newest. Without it `useTruemainMatches`
— which refires on page, position and championId — can let a slow page-3 response land after page 4's and
write its rows under a pager reading 4 — #1234.

- **A row rendered on more than one surface sizes off its own width, not the viewport** (#967).
  `MatchRow` and `LeaderboardRow` are `@container`s. The same row sits full-width on a page, in a ~33rem
  drawer and in a sidebar, so a viewport `xl:` breakpoint told the narrow copy it owned the page and its
  fixed columns spilled into its own `overflow-hidden` clip — invisible on the surface it was tuned for,
  broken everywhere else. Content degrades by tier as the row narrows (compositions, then secondary stats,
  then the loadout wrapping onto a second line) rather than being cut off. `pages/dev/match-row.vue` renders
  the row at each tier width so the compact layouts are reviewable without reproducing the host surface.

- **A tooltip trigger keeps the same DOM element for the life of the component.** Reka reads the trigger node
  once, in `onMounted`, and binds the hoverable-content "grace area" `pointerleave` to that snapshot. A trigger
  that swaps its root element later — a `v-if` icon / `v-else` fallback box flipping over when the static data
  lands — leaves that listener on a detached node, so the tooltip opens normally and can then *never* close on
  pointer exit: sweeping across a row of icons piled every tooltip it touched on screen. Icon components that
  can render before their data therefore keep one unconditional root (`SkeletonImage`, which draws the text
  fallback itself via its `fallback` prop) instead of two branches. Item and perk icons never had the bug —
  they always rendered a single `SkeletonImage` — which is why the symptom looked specific to skill orders and
  summoners.

- **A skeleton is the real component in `pending` mode, not a drawing of it.** The champion page's build
  section has two loading phases it cannot merge: the aggregate and the patch-pinned static bundles are
  separate fetches, and the ~95 DDragon icons only start downloading once the ids they resolve are mounted.
  So the reader sees *a* placeholder while the API answers, then the real panels with every icon still pulsing
  while the images land. `ChampionBuildTabsSkeleton` used to be a hand-drawn stack of grey blocks sized to the
  measured real heights: it reserved the space, but it was a second, unrelated picture, so a cold load visibly
  rebuilt itself the moment the API answered. It now renders `ChampionBuildTabs` itself over a placeholder
  aggregate (`app/utils/build-placeholder.ts`) with `pending` set — unresolvable ids, so every icon falls back
  to the same pulsing box `SkeletonImage` already draws mid-load, and every number is masked (`RateBadge`,
  the tab pickrate) rather than printing the placeholder's filler figures. The two phases become one
  continuous state whose only transition is the content filling in, the skeleton cannot drift when a section
  moves, and CLS is exact instead of estimated. `pages/dev/build-skeleton.vue` renders both skeletons with
  nothing to fetch, the same way `dev/match-row.vue` makes a row reviewable in isolation. The escape hatch
  is per-section: the skeleton takes `powerspikes` because the player-scoped page's tabs carry no population
  scope and its real card has no such section to reserve.

- **Icon slots are rendered from the ids, never gated on a resolved static lookup.** Same rule as the
  tooltip-trigger one above, from the other side. The build tabs' leading item/keystone icons were gated on
  `itemsMap[id]` / `runeTree.perks[id]`, so the whole tab bar reflowed when those deferred (~370 KiB, patch-
  pinned) payloads landed — and swapping the trigger element that late is exactly the case that leaves a Reka
  tooltip unable to close. The id is what answers "is there something here"; `SkeletonImage` already draws the
  loading box for a null icon. `itemSlots()` in `shared/utils/build.ts` exists for the same reason.

**Champion-page icons are slow because of browser queue depth, not the image proxy — measure the split before
"optimising" it.** The obvious reading of a slow champion page (~118 `/_ipx/**` requests, ~600 KB) is that the
proxy or Riot's CDN is slow. Splitting per-request timing on preprod says otherwise: **queue 2459 ms, server
65 ms, download 1 ms**, and the proxy answers 40 concurrent requests in 0.65 s. The cost is the browser holding
a burst of ~106 distinct, equal-priority image requests issued in one tick when the API data lands. So a
persistent/disk cache and a boot-time pre-warm were both **rejected**: they buy back tens of milliseconds of an
850 ms budget, while a disk cache on a public, unauthenticated route that accepts arbitrary modifiers is the
same disk-exhaustion class that already crash-looped this box (#680). Pre-warming was rejected additionally
because it fires ~500 requests at Riot and the volunteer-run CommunityDragon mirror on **every** boot, and this
stack has had restart loops. What is left is payload size and queue depth — #997.

**`SkeletonImage` serves WebP; `RankIcon` deliberately does not.** At the canonical 64×64 fetch size the live
assets go champion 10194 B → 1100 B, perk 8933 B → 3396 B, item 6096 B → 2130 B with no visible difference —
the perk icons (thin bright line art over transparency) are the demanding case and survive it. It is **not**
applied globally: `RankIcon`'s sources are `.svg` and IPX passes them through as `image/svg+xml` today, so
forcing a raster format would trade a vector that stays crisp at any DPR for a 20 px bitmap. This is a
format decision inside the existing `<img>` + `useImage()` split, not a change to it — the `@nuxt/image`
policy (fixed-size icons use `<img>` + `useImage()`, real responsive images use `<NuxtImg>`) still stands.

**Every icon URL is built by one helper, so one asset is one cache entry.** `useCanonicalIcon()` is the
only place that decides fetch size and format; `SkeletonImage` calls it, and so does each component that
deliberately renders a plain `<img>` instead (lane glyphs in leaderboard/profile rows and match rows, the
search palette's trailing icons — fixed-size glyphs appearing dozens of times per page, where one
component instance per icon costs more than it gives). Hand-writing `ipx(...)` per call site is what this
replaces, and the drift was real: the same position glyph was being fetched at 12, 20, 22 *and* 64 px —
four downloads and four cache entries for one image — while the search palette bound the **raw Data
Dragon URL**, shipping a 120×120 PNG (30 267 B, straight from Riot's CDN, uncached by us) into a 20 px
box. Measured on preprod after the change, that icon arrives in **1 446 B**.

Note the number the canonical size deliberately gives up: fetched at the palette's own 20 px it would be
306 B, roughly five times smaller again. It is fetched at 64 px anyway, because a *second* size is a
second cache entry — the same champion portrait already exists at 64 px from every other page, so the
canonical URL is usually a cache hit costing nothing, while a bespoke 20 px variant would always be a
fresh download. Sizing per call site is the local optimum and the global mistake; that is the whole point
of the helper. `RankIcon` remains the one deliberate exception, for the SVG reason above — #1000.

**The `/_ipx/**` cache evicts by patch, keeping the current patch and the two before it.** Every source URL is
patch-pinned, so a release turns the whole catalogue over at once and strands the outgoing patch's bytes in the
64 MB budget precisely when the cache is cold. The sweep runs **only** when a newer patch is first observed, not
as a check on every write: the champion page has a patch filter, so old-patch URLs are legitimate traffic, and
evaluating expiry per write would store and immediately drop each of their icons, leaving old-patch browsing
permanently uncached. The window is the three newest patches *observed* rather than `newest - 2` arithmetic, so
a season rollover (16.1 after 15.24) keeps the right three — `server/utils/ipx-patch-retention.ts`, #997.

## `web/` and `admin/` duplicate their Data Dragon helpers on purpose, and the copies are labelled (2026-08-26)

The two apps are deliberately separate — different auth, different rendering mode (`ssr: false` in the admin),
different deploy — and there is no shared package to hold common code. That is not changing: a package would
couple two release cadences to save a few dozen lines. But two files *were* copied between them and then
drifted **in both directions**, which is the failure mode worth guarding against, not the duplication itself.

By the time it was caught (#1226), `server/api/static/champions.get.ts` existed twice with each copy carrying a
fix the other was missing. The admin had re-inlined an **uncached** `resolveLatestPatch()`, undoing #947 — and
worse there than on the web, because the admin renders client-side, so that DDragon round trip ran once per
page load rather than once per SSR. Meanwhile the admin had added a `?patch=` format guard that the web — the
only *public* app — never received. `shared/utils/ddragon.ts` had drifted too: same code, comments edited
independently on each side, and the #966 alternate-mode floor pinned by a test on the web side only.

The rule that came out of it: a file duplicated across the two apps **says so in a header naming its twin**, and
the behaviour it encodes is pinned by a test in *both* suites. Labelled copies are `shared/utils/ddragon.ts`,
`server/utils/ddragon-patch.ts` and `server/api/static/champions.get.ts`; the champion handlers differ only by
the admin's `requireUserSession` gate, so any other difference in a diff is a regression, not a variant.

`PATCH_PATTERN` (`^\d+\.\d+\.\d+$`) sits next to `normalizeDataDragonPatch`, which produces the value it
validates — that function expands the short `16.5` form the backend scopes expose and passes everything else
through untouched, so it is a shape fixer and never a guard. Every static endpoint interpolates the result into
a CDN URL *and* uses it as a cache key, so an unvalidated `?patch=` is both a path-injection vector and an
unbounded-cache-key vector: one entry per distinct string, held for the payload TTL. The guard lives in
`normalizeRequestedPatch` and covers all four web static endpoints, not just the champion list.
