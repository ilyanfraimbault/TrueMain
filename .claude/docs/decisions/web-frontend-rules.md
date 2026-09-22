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

The rule covers anything else an immediate watcher can trigger, not just fetches. Its worked example was
`useErrorToast`, which registered its watcher under `import.meta.client`: the `if (!value) return` guard made
the SSR run a no-op only because every error ref wired to it happened to come from a `server: false` fetch —
true, unwritten, and untrue the day one was pointed at the server-rendered build summary or the leaderboard,
where a toast pushed during SSR serialises into the payload and pops up unprompted for every visitor served
that render — #1234. **The composable is gone** (#1661 removed the toast-on-page-load surface entirely), so
the example is history; the rule it illustrates is not. An immediate watcher that touches `useToast`, a
cookie or any other client-shaped state still has to be made client-only *structurally* — `import.meta.client`
is a build-time constant, so the watcher does not exist on the server at all — rather than relying on its
body happening to no-op there.

**A closed `enabled` gate resolves `success` with an empty model, so the gated composables expose their own
`pending`.** `createChampionPatchSlice` and `useChampionTrend` hold their request
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

  Since #1585 the icon tooltips are not mounted until the pointer first passes over the icon
  (`GameTooltip/LazyTooltip.vue`). That keeps this rule: Reka takes its snapshot when the tooltip mounts, once,
  on the element it keeps for good, and it opens on `pointermove`, so the resting pointer opens it on its next
  move and nothing is opened by hand.

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
  nothing to fetch, the same way `dev/match-row.vue` makes a row reviewable in isolation.

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

**A local Nuxt layer was weighed and rejected for now (2026-09-17, #1623).** The argument against a package does
not apply to a layer: a local layer has no version and is never published. The layer loses on build plumbing
instead. The images build from `./web` and `./admin` contexts, so a root-level `layers/` directory is invisible
to both Docker builds. Each app also has its own `node_modules` and the repo root has none, so bare imports inside
layer files have nothing to resolve against. Adopting a layer would mean moving both production builds to the
repo root, giving the layer its own dependency story, and making the CI `changes` gate run both apps for it. All
of that to share about 300 near-identical, rarely touched lines (`proxy-path`, `abandoned-request`,
`log-forwarder`, `log-forwarding`). The pairs that really drifted need their differences reconciled whatever the
mechanism, and a layer does not do that for them. Worth revisiting if the shared surface grows substantially, or
if the image builds move to the repo root for another reason.

The guard against drift will be a CI check (#1625, not yet shipped): the twin pairs are declared, and a pair that differs outside
lines marked app-specific fails the build.

`PATCH_PATTERN` (`^\d+\.\d+\.\d+$`) sits next to `normalizeDataDragonPatch`, which produces the value it
validates — that function expands the short `16.5` form the backend scopes expose and passes everything else
through untouched, so it is a shape fixer and never a guard. Every static endpoint interpolates the result into
a CDN URL *and* uses it as a cache key, so an unvalidated `?patch=` is both a path-injection vector and an
unbounded-cache-key vector: one entry per distinct string, held for the payload TTL. The guard lives in
`normalizeRequestedPatch` and covers all four web static endpoints, not just the champion list.

## SSR calls to the site's own `/api` forward the visitor, and a failure is never cached as an answer (2026-09-14)

**Decision:** a fetch that can run during SSR and reaches the backend through `/api` goes through
`useRequestFetch()` (or `useFetch`, which already does), and a server handler that fans out to `/api` forwards
the visitor's `X-Forwarded-For` — that header only. A cached server function stores an upstream's 404, but
rethrows a 429, a 5xx or an unreachable upstream so that nothing is stored — #1557.

On the server, a bare `$fetch('/api/…')` is an in-process call that carries none of the incoming request's
headers. The proxy then calls the API from the web container with no `X-Forwarded-For`, and the API — which keys
its rate limit on that header since #1546 — puts every SSR call of every visitor in one bucket: the site-wide
ceiling #1546 removed for browser calls. The `/truemains` leaderboard's first page and the champion page's build
summary were the two SSR paths affected; every other page fetch is `server: false`, and the static endpoints talk
to Data Dragon, not the API.

- **Only that one header crosses into a cached handler.** The champion summary is shared by everyone who hits
  the same slice key; forwarding the rest of the triggering visitor's request (cookies, `If-None-Match`) into a
  response other people receive is how a cache serves one person's variant to all. The visitor whose view misses
  the cache is the one charged for the fan-out, which is what the limit is for.
- **A 429 used to be cached for five minutes as an empty summary.** Every upstream failure was folded into
  `null`, and `null` is what the cache stored, so one throttled request blanked the paragraph for the whole slice.
  A 404 still degrades and is cached — it is the slice's answer. Anything else fails the cached loader, and that
  one view gets the empty summary, without a second fan-out against an API that just asked for less traffic.
- **The proxy does not rewrite `X-Forwarded-For` from `getRequestIP`.** h3 returns the header's *first* entry,
  the one a client writes; the API reads the *last*, the one the edge appends. Normalising it in the proxy would
  reopen exactly the spoofing #1546 closed, so the header passes through untouched and the edge stays the only
  writer that counts.

## Static game data is cached by the browser for the hour the server caches it (2026-09-15)

**Decision:** successful `/api/static/*` answers carry `Cache-Control: public, max-age=3600,
stale-while-revalidate=86400`, added by a Nitro `beforeResponse` hook, never to an error — #1584.

- **Why.** A reload of a prod champion page downloaded 684 KB of static data again (7 of 12 calls), and its icons
  waited for it, while bundles and images came from the browser cache. The client cache
  (`app/utils/static-cache.ts`) dies with the page.
- **The same hour as the server cache**, so a new patch reaches a visitor no later than the server itself serves
  it; `stale-while-revalidate` keeps a reload past the hour from blocking on the network.
- **A hook, not a route rule**, because route-rule headers are set before the handler and would make a failed
  lookup cacheable too.

## A champion page builds only what is on screen: hidden build tabs and unhovered tooltips wait (2026-09-15)

**Decision:** a build tab's panel is mounted the first time the tab is opened and kept afterwards, and the item,
rune and spell icon tooltips mount on the first hover — #1585.

- **Why.** A prod champion page spent about 9.6 s of main-thread long tasks in its first 14 s (longest 2.1 s),
  delaying its second wave of fetches by 2 s after the data it needed had arrived. A CPU profile of the page
  put the time in Vue's component creation and patching and in Nuxt UI / Reka's per-component prop, context and
  class work, not in the app's own code: the cost was the number of components. Three build panels were fully
  mounted on load, and every icon was a `UTooltip`.
- **Kept after first open**, so switching back to a tab is instant and keeps its state; a new set of builds
  starts over on its first tab.


## Focus moves to the content only when the path changes (2026-09-17)

**Decision:** after a client-side navigation, focus moves to `#main-content` (the `UMain` in `app.vue`) only when
the path changed and the navigation is not the initial one; the skip link focuses the same region without writing a
hash to the URL — #1616.

- **Why path, not any navigation.** The champion page's filters, the leaderboard pager and the player page's match
  filters are `router.replace` calls on the same path. Moving focus there would pull the keyboard out of the control
  the reader is still operating, one click at a time.
- **After `page:finish`, not in `afterEach`.** The guard runs before the new page is mounted; focusing then parks the
  keyboard in front of the outgoing page.
- **`preventScroll` on both paths.** `<main>` starts under the sticky header, so `focus()` with scrolling aligned its
  top with the viewport's and hid the page's first row behind the header; the next Tab scrolls to its own target.
- **One `<main>`, owned by the shell** (#1615). Pages render a single non-landmark root: two nested `main` landmarks
  made "jump to main" ambiguous, and page transitions need a single root element.

## Render-time behaviour is tested inside the Nuxt runtime, in a vitest project of its own (2026-09-17)

**Decision:** `web/` runs two vitest projects from one `npm test`: `unit` (pure functions, bare happy-dom, no
Nuxt) and `nuxt` (`@nuxt/test-utils`, `defineVitestProject`), whose tests live in `web/tests/nuxt/` — #1620.

- **Why.** The bugs that cost the most on this app were render-time ones — lazy-hydration mismatches
  (#834/#837), an immediate watcher firing during SSR (#1234) — and the unit suite has no runtime to mount a
  component in, so none of them could be asserted.
- **Separate projects**, so the runtime's boot cost never lands on the fast suite and a runtime flake never
  blocks a pure-function test.
- **What the runtime suite pins today**: the champion build section's skeleton → tabs transition, driven by
  the real `useChampion`, and `useLazyHydrationSnapshot` hydrating against its SSR value — with a control test
  proving the harness does report a mismatch when the live value is bound directly.
- **A test that needs auto-imports, `#components`, `useState`, routing or a hydration path goes in
  `tests/nuxt/`**; everything else stays a unit test.

## Backend calls go through `useApi` / `useApiFetch`, not a bare `$fetch('/api/…')` (2026-09-17)

**Decision:** `app/composables/useApi.ts` holds the one way the web app calls the backend — #1619.
`useApi` (built with `createUseFetch`) is the default for a declarative call; `useApiFetch()`, called in setup,
is the fetcher for a `useAsyncData` handler that needs logic (a 404 that means "empty", a gate resolving a
placeholder). Paths are relative to `/api`, which both helpers own and a caller cannot override.

- **Why.** The #1557 rule (an SSR-capable call must go through `useRequestFetch()` so the visitor's
  `X-Forwarded-For` reaches the API's rate limiter) was enforced by memory. Both helpers resolve
  `useRequestFetch()` themselves, so a new call site forwards by default. `tests/nuxt/use-api.test.ts` pins it
  by swapping `useRequestFetch` for a spy, and `tests/api-fetch/no-bare-api-fetch.test.ts` fails on any new bare
  `$fetch('/api/…')` outside a list that may only shrink.
- **Errors are normalised once, after ofetch's retry.** An HTTP failure is rethrown with its status and the
  `describeFetchError` copy as its message — never the proxied URL or the backend's body — so a stray
  `{{ error.message }}` or `error.vue` cannot print either. The status survives, so handlers still branch on
  `fetchErrorStatus`. A failure without a status (network drop, abort) passes through untouched: Nuxt recognises a
  superseded request by its `AbortError`. Normalising in an `onResponseError` hook was rejected — throwing there
  skips ofetch's retry of a 5xx / 429 GET.
- **Keys are unchanged by a migration.** A migrated call passes its old key explicitly (`useApi(…, { key })`), and
  calls sharing a key keep identical options (Nuxt 4's singleton data-fetching rule).
- **The hand-rolled fetchers stay apart** — `useTruemainFetch` and its consumers, `useCompositionBuild`,
  `useCompositionBuildGames`, `useTruemainSearch`: per-viewer payloads, client-only by construction, with
  monotonic request tokens. `useFetch`'s shared payload is exactly what they must never enter (#862, #1234).

## The three text pages are cached at runtime (`swr`), never prerendered (2026-09-18)

**Decision:** `/about`, `/privacy` and `/terms` carry a `swr` route rule of one hour — Nitro renders them once
per container and serves the cached HTML afterwards, revalidating in the background. `prerender: true` was
tried first (#1617) and rejected.

- **Why not prerender.** Nuxt inlines `runtimeConfig.public` into the rendered HTML, so a page emitted by
  `nuxt build` carries the *build* environment's config and the client keeps it for the whole visit. Measured
  on a production build served with preprod's env: a visit landing on `/about` loaded no Umami script on that
  page *or any page reached from it*, lost the footer's `env · version` stamp, and emitted the prod canonical
  URL. That reverses the promotable-image decision (`runtimeConfig.public` is read at runtime so one image
  moves from preprod to prod) — re-reading each value on the client instead would leave the same trap for the
  next `runtimeConfig.public` consumer.
- **Why one hour.** The TTL matches the champion slug map's own server cache
  (`server/api/static/champion-slugs.get.ts`), the only backend-derived value in these pages' payload, so a
  cached page is never staler than a freshly rendered one.
- **Measured**: served locally, a cached response answers in ~3.5 ms against ~117 ms for a full render, and
  the env, version, Umami host/id and canonical URL are the running container's.
