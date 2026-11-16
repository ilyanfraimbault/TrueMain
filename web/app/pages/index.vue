<script setup lang="ts">
import { formatCompactCount } from '~~/shared/utils/counts'
import { isLoadingStatus } from '#common/utils/async-data'

// The homepage title leads with the brand, so opt out of the global
// `%s · TrueMain` template — it would duplicate the name in search results.
useHead({ titleTemplate: null })
useSeoMeta({
  title: 'TrueMain · Champion builds from real mains',
  description: 'League of Legends champion builds, runes and skill orders from true main players.',
})

// Homepage snapshot for the active patch (#972): the true games-analyzed
// total plus a short, pre-sorted slice of the top tier-list rows — drives both
// the hero stat chips and the tier-list panel. A dedicated endpoint
// (`GET /champions/overview`) rather than the full ~500-row `/champions`
// directory, so the homepage never fetches or sorts data it only shows 8
// rows and two numbers of.
const overviewFetch = useChampionOverview()
const { data: overview, status: overviewStatus } = overviewFetch

// Shared static list (champion id/name/icon) — same cache key as the unified
// search and the other pages, so the prefetch-warmed payload is reused.
const { data: staticList, status: staticListStatus } = useChampionStaticList()

const championsById = useChampionsById(staticList)

const { data: versions } = useDDragonVersions()
const ddragonPatch = computed(() => versions.value?.[0] ?? null)

const servedPatch = computed(() => overview.value?.patchVersion ?? null)

// The tier-list panel needs both the overview (rows + tier/WR/PR) and the
// static list (names + icons) — a single loading condition owned by the page,
// so the panel and its skeleton never each decide independently and disagree.
// Both sources are `server: false`, so this starts `true` identically on the
// server and the client's first render (same mechanism `overviewPending`
// below already relies on for the hero stat chips), keeping hydration clean —
// no intersection-observer, no dynamic import, so there is no chunk-loading
// window where neither the skeleton nor the real panel is on screen.
const tierlistPending = computed(() =>
  isLoadingStatus(overviewStatus.value) || isLoadingStatus(staticListStatus.value))

// ─── Truemains teaser (SSR, like the /truemains page) ─────────────────────
const {
  rows: truemainRows,
  total: truemainsTotal,
  isInitialLoading: truemainsInitialLoading,
  ready: truemainsReady,
} = useTruemainsLeaderboard(1, { pageSize: TRUEMAINS_TEASER_ROWS })

const trackedTruemains = computed(() =>
  truemainsTotal.value > 0 ? truemainsTotal.value : null)

// ─── Hero stat chips — every number is derived from a real payload ────────
const overviewPending = computed(() => isLoadingStatus(overviewStatus.value))

// Lifetime totals, printed without a patch qualifier — deliberately, on both
// counts. The volume chips used to name the patches they covered ("over
// 16.16–16.15", #1109) because the figure *was* patch-scoped and a bare number
// would have implied more than it measured; the backend now answers with every
// game it holds, so there is nothing left to qualify and the chip reads as the
// claim it was always making. It also removes the trap the patch label papered
// over: a patch-scoped total falls by an order of magnitude on patch day, which
// reads as data loss. This one only grows.
const gamesAnalyzed = computed(() => overview.value?.gamesAnalyzed ?? 0)

// A client-side navigation keeps the outgoing page under the loading bar until
// the API has answered (#1689); the static lookups keep their skeleton.
await Promise.all([overviewFetch, truemainsReady])
</script>

<template>
  <div>
    <!-- Hero — the one place the eclipse shader renders. It is mounted here
         rather than in `app.vue` so it is bounded by this section: as a
         viewport-fixed layer it bled through the champion and leaderboard
         tables and made rows change luminance down the length of the list.
         `relative` + `overflow-hidden` give the absolutely-positioned backdrop
         its bounds, and the fade below hands off to the flat page background so
         the corona doesn't stop at a hard edge. The negative top margin (the
         floating header's full height) slides the hero up under that bar, so
         the eclipse runs to the top of the viewport behind it. -->
    <section class="relative -mt-19 overflow-hidden">
      <AppBackdrop />
      <div
        aria-hidden="true"
        class="pointer-events-none absolute inset-x-0 bottom-0 -z-10 h-32 bg-gradient-to-b from-transparent to-default"
      />
      <div class="relative mx-auto flex max-w-3xl flex-col items-center px-6 pb-16 pt-40 text-center sm:pb-24 sm:pt-52">
        <h1 class="text-4xl font-semibold leading-[1.1] tracking-tight text-balance text-highlighted sm:text-5xl">
          League builds from the players who main the champion
        </h1>
        <p class="mt-5 max-w-xl text-base leading-relaxed text-muted sm:text-lg">
          Items, runes and skill orders computed from true mains' ranked games, not from everyone who picked the champion once.
        </p>

        <!-- Search is SSR'd as a plain field, but its command-palette JS
             (UModal + UCommandPalette + the truemains search composable) is
             heavy and never needed for the first paint. Delay its hydration to
             browser-idle so that chunk stays off the critical path. The
             trigger renders no client-only data here (no champion filter on the
             homepage), so the SSR markup and the eventual client render are
             identical — the one part that could not be, the platform-dependent
             ⌘/Ctrl hint, is `<ClientOnly>` inside the component. ⌘K is owned by
             the always-mounted header instance, so deferring this one doesn't
             affect the shortcut. -->
        <LazyAppSearch
          variant="field"
          size="lg"
          class="mt-8 w-full max-w-xl"
          hydrate-on-idle
        />

        <!-- Real numbers only, skeleton until their source payload resolves.
             Both are lifetime totals, so neither names a patch (#1109). -->
        <dl class="mt-6 flex flex-col items-center justify-center gap-x-2 gap-y-1 sm:flex-row text-sm text-muted">
          <USkeleton
            v-if="overviewPending"
            class="h-4 w-40"
          />
          <div
            v-else-if="gamesAnalyzed > 0"
            class="flex gap-1"
          >
            <dt class="sr-only">
              Main games analyzed
            </dt>
            <dd>
              <span class="font-semibold tabular-nums text-default">{{ formatCompactCount(gamesAnalyzed) }}</span> main games analyzed
            </dd>
          </div>
          <span
            v-if="!overviewPending && gamesAnalyzed > 0 && trackedTruemains !== null"
            aria-hidden="true"
            class="hidden text-dimmed sm:inline"
          >·</span>
          <div
            v-if="trackedTruemains !== null"
            class="flex gap-1"
          >
            <dt class="sr-only">
              Truemains tracked
            </dt>
            <dd>
              <span class="font-semibold tabular-nums text-default">{{ formatCompactCount(trackedTruemains) }}</span> truemains tracked
            </dd>
          </div>
        </dl>
      </div>

    </section>

    <!-- The desktop app, announced before the panels it brings into champion
         select. -->
    <HomeDesktopBetaPanel />

    <!-- Live data panels — equal-width halves so the two read as a balanced
         pair and the truemains rows have room for champion + play-rate
         without truncating names. -->
    <section class="mx-auto max-w-6xl px-4 pb-20 md:px-6">
      <div class="mb-6 flex flex-wrap items-baseline gap-x-3 gap-y-1">
        <h2 class="text-2xl font-semibold tracking-tight text-highlighted">
          {{ servedPatch ? `Patch ${servedPatch}` : 'This patch' }}
        </h2>
        <p class="text-sm text-muted">
          The most played and banned champions, and the players who main them.
        </p>
      </div>
      <div class="grid gap-6 lg:grid-cols-2">
        <!-- Tier list: rendered eagerly (no chunk to fetch on demand, no
             intersection-observer gate — #972), so there is no window where
             neither the skeleton nor the real panel is on screen. -->
        <HomeTierlistPanelSkeleton v-if="tierlistPending" />
        <HomeTierlistPanel
          v-else
          :top-rows="overview?.topRows ?? []"
          :champions-by-id="championsById"
        />
        <!-- Truemains teaser stays eagerly SSR'd + immediately hydrated: its rows
             come from a `server: true` fetch, and its profile-icon `v-if`/`v-else`
             and champion enrichment resolve from `server: false` sources, so
             delaying its hydration would flip those branches after the data lands
             and cause a structural hydration mismatch. The ~373 KiB item map it
             needs is instead deferred inside the panel (visibility-gated fetch). -->
        <HomeTruemainsPanel
          :rows="truemainRows"
          :champions-by-id="championsById"
          :initial-loading="truemainsInitialLoading"
          :patch="ddragonPatch"
        />
      </div>
    </section>
  </div>
</template>

