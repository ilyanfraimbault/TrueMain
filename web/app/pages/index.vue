<script setup lang="ts">
import type { RegionSlug } from '~~/shared/types/leaderboard'
import { formatCompactCount } from '~~/shared/utils/counts'
import { isLoadingStatus } from '~/utils/async-data'

// The homepage title leads with the brand, so opt out of the global
// `%s · TrueMain` template — it would duplicate the name in search results.
useHead({ titleTemplate: null })
useSeoMeta({
  title: 'TrueMain — Champion builds from real mains',
  description: 'League of Legends champion builds, runes and skill orders from true main players.',
})

// Homepage snapshot for the active patch (#972): the true games-analyzed
// total plus a short, pre-sorted slice of the strongest rows — drives both
// the hero stat chips and the tier-list panel. A dedicated endpoint
// (`GET /champions/overview`) rather than the full ~500-row `/champions`
// directory, so the homepage never fetches or sorts data it only shows 8
// rows and two numbers of.
const { data: overview, status: overviewStatus } = useChampionOverview()

// Shared static list (champion id/name/icon) — same cache key as the unified
// search and the other pages, so the prefetch-warmed payload is reused.
const { data: staticList, status: staticListStatus } = useChampionStaticList()

const championsById = useChampionsById(staticList)

const { data: versions } = useDDragonVersions()
const ddragonPatch = computed(() => versions.value?.[0] ?? null)

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
const region = ref<RegionSlug | null>(null)
const {
  rows: truemainRows,
  total: truemainsTotal,
  isInitialLoading: truemainsInitialLoading,
  isLoading: truemainsLoading,
} = useTruemainsLeaderboard(1, { pageSize: 5, region })

// "Truemains tracked" chip: read the live total while the region filter is
// off, and fall back to a latched copy once the user flips the region tabs
// (the filtered total is the region's count, not the global one). The latch
// is client-only and the computed reads `total` directly — a watcher-set ref
// would stay null in the SSR HTML (watchers don't flush during SSR) while
// hydration sets it synchronously, a guaranteed node mismatch.
const latchedTotal = ref<number | null>(null)
if (import.meta.client) {
  watch(truemainsTotal, (value) => {
    if (region.value === null && value > 0) latchedTotal.value = value
  }, { immediate: true })
}
const trackedTruemains = computed(() =>
  region.value === null && truemainsTotal.value > 0
    ? truemainsTotal.value
    : latchedTotal.value)

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

// ─── Server-rendered champion links (#1209) ───────────────────────────────
// The homepage is the only page on the site with any authority, and it passed
// none of it on: every champion link it holds lives inside the client-only tier
// panel, so the HTML a crawler receives contained not one `/champions/{slug}`
// anchor. The site's ~174 champion URLs existed only in `sitemap.xml`, which on
// a young site with no backlinks does not get them indexed.
//
// Twelve, not the full A→Z index `/champions` renders: the front door should
// point at the pages worth entering by, and 174 links here would dilute every
// one of them. Awaited server-side only — the app has no Suspense fallback on
// `<NuxtPage>`.
const championIndexFetch = useChampionIndexTiers({ limit: 12 })
if (import.meta.server) await championIndexFetch
const { data: championIndex } = championIndexFetch

</script>

<template>
  <div>
    <!-- Hero — the one place the eclipse shader renders. It is mounted here
         rather than in `app.vue` so it is bounded by this section: as a
         viewport-fixed layer it bled through the champion and leaderboard
         tables and made rows change luminance down the length of the list.
         `relative` + `overflow-hidden` give the absolutely-positioned backdrop
         its bounds, and the fade below hands off to the flat page background so
         the corona doesn't stop at a hard edge. -->
    <section class="relative overflow-hidden">
      <AppBackdrop />
      <div
        aria-hidden="true"
        class="pointer-events-none absolute inset-x-0 bottom-0 -z-10 h-32 bg-gradient-to-b from-transparent to-default"
      />
      <div class="relative mx-auto flex max-w-3xl flex-col items-center px-6 pb-16 pt-20 text-center sm:pb-24 sm:pt-28">
        <p class="text-sm font-medium text-primary">
          Champion intelligence
        </p>
        <h1 class="mt-4 text-4xl font-semibold leading-[1.05] tracking-tighter text-highlighted sm:text-6xl">
          Real builds from<br>
          <span class="text-primary">real mains</span>.
        </h1>
        <p class="mt-5 max-w-xl text-base leading-relaxed text-muted sm:text-lg">
          Builds, runes and skill orders from the players who truly mastered your champion.
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
          class="mt-9 w-full max-w-xl"
          hydrate-on-idle
        />

        <!-- Stat chips: real numbers only, skeletons until their source
             payload resolves. Two of them, both lifetime totals — the third
             ("N champions ranked") was dropped with the patch labels: it is the
             one figure here that is inherently patch-scoped, so it could not
             join the other two without dragging a qualifier back in. -->
        <dl class="mt-8 flex flex-wrap items-center justify-center gap-x-7 gap-y-3 text-sm">
          <div class="flex items-center gap-2">
            <UIcon
              name="i-lucide-database"
              class="size-4 text-primary"
            />
            <USkeleton
              v-if="overviewPending"
              class="h-4 w-32"
            />
            <template v-else-if="gamesAnalyzed > 0">
              <dt class="sr-only">
                Main games analyzed
              </dt>
              <dd class="text-muted">
                <span class="font-semibold tabular-nums text-default">{{ formatCompactCount(gamesAnalyzed) }}</span> main games analyzed
              </dd>
            </template>
          </div>

          <div
            v-if="trackedTruemains !== null"
            class="flex items-center gap-2"
          >
            <UIcon
              name="i-lucide-users"
              class="size-4 text-primary"
            />
            <dt class="sr-only">
              Truemains tracked
            </dt>
            <dd class="text-muted">
              <span class="font-semibold tabular-nums text-default">{{ formatCompactCount(trackedTruemains) }}</span> truemains tracked
            </dd>
          </div>
        </dl>
      </div>
    </section>

    <!-- Live data panels — equal-width halves so the two read as a balanced
         pair and the truemains rows have room for champion + play-rate
         without truncating names. -->
    <section class="mx-auto grid max-w-6xl gap-6 px-4 pb-20 md:px-6 lg:grid-cols-2">
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
        v-model:region="region"
        :rows="truemainRows"
        :champions-by-id="championsById"
        :initial-loading="truemainsInitialLoading"
        :loading="truemainsLoading"
        :patch="ddragonPatch"
      />
    </section>

    <!-- The one block of champion links in the server-rendered HTML. Outside
         any `<ClientOnly>` and fed by an SSR-enabled fetch carried in the Nuxt
         payload, so hydration reads the same object the server rendered from.
         It doubles the tier panel above it in content, on purpose: the panel is
         portraits and numbers, this is the names — and the names are what a
         crawler (and a reader scanning for one champion) can actually use. -->
    <section class="mx-auto max-w-6xl px-4 pb-20 md:px-6">
      <ChampionTierLinks
        :tiers="championIndex.tiers"
        :patch="championIndex.patch"
        title="Strongest champions right now"
      />
    </section>

    <!-- CTA -->
    <section class="border-t border-default/60">
      <div class="mx-auto max-w-3xl px-6 py-16 text-center sm:py-20">
        <h2 class="text-2xl font-semibold tracking-tight sm:text-3xl">
          Find <span class="text-primary">your</span> real build.
        </h2>
        <p class="mx-auto mt-3 max-w-xl text-base text-muted">
          Open the champion you actually play and see what their mains are buying this patch.
        </p>
        <div class="mt-7 flex flex-wrap justify-center gap-3">
          <UButton
            to="/champions"
            color="primary"
            size="lg"
            icon="i-lucide-swords"
            label="Explore champions"
          />
          <UButton
            to="/truemains"
            color="neutral"
            variant="subtle"
            size="lg"
            icon="i-lucide-trophy"
            label="Truemains leaderboard"
          />
        </div>
      </div>
    </section>
  </div>
</template>
