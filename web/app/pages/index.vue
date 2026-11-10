<script setup lang="ts">
import { formatCompactCount } from '~~/shared/utils/counts'
import { isLoadingStatus } from '#common/utils/async-data'
import { championDisplayName } from '~~/shared/utils/champion-name'

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

// The hero's art is the splash of the champion leading this patch's "most
// played and banned" slice: game art that says something about the data under
// it, in place of the decorative eclipse and star field (#2552). Client-side
// only, like both of its sources; the hero reads complete without it.
const heroChampion = computed(() => {
  const row = overview.value?.topRows[0]
  const champ = row ? championsById.value.get(row.championId) : undefined
  return row && champ ? { id: row.championId, name: championDisplayName(row.championId, champ), iconUrl: champ.iconUrl } : null
})
const heroSplash = useChampionSplash(() => heroChampion.value?.iconUrl)
const { pathFor } = useChampionSlugs()
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
    <!-- Hero. The negative top margin (the floating header's full height)
         slides it up under that bar, so the splash runs to the top of the
         viewport behind it. Left-aligned on the page's own grid, like every
         section under it. -->
    <section class="relative -mt-19 overflow-hidden border-b border-default/60">
      <!-- The art sits on the right, faded into the page on its left edge, so
           its subject stays clear of the headline on wide screens; on a phone
           it is a dim wash behind the text. -->
      <img
        v-if="heroSplash"
        :src="heroSplash"
        alt=""
        aria-hidden="true"
        decoding="async"
        class="hero-splash absolute inset-y-0 right-0 h-full w-full object-cover object-[50%_20%] md:w-[70%]"
      >
      <div
        aria-hidden="true"
        class="pointer-events-none absolute inset-0 bg-linear-to-r from-default/70 to-transparent md:from-transparent"
      />
      <div
        aria-hidden="true"
        class="pointer-events-none absolute inset-0 bg-linear-to-t from-default via-default/10 to-default/50"
      />

      <div class="relative mx-auto max-w-6xl px-4 pb-14 pt-32 md:px-6 sm:pb-20 sm:pt-40">
        <h1 class="max-w-2xl text-4xl font-semibold leading-[1.1] tracking-tight text-balance text-highlighted sm:text-5xl">
          League builds from the players who main the champion
        </h1>
        <p class="mt-4 max-w-xl text-base leading-relaxed text-muted sm:text-lg">
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
        <dl class="mt-5 flex flex-wrap items-center gap-x-2 gap-y-1 text-sm text-muted">
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
            class="text-dimmed"
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

      <!-- Whose art this is, and why it is here. -->
      <p
        v-if="heroChampion && heroSplash"
        class="absolute inset-x-0 bottom-4 mx-auto hidden max-w-6xl px-4 text-right text-xs text-dimmed sm:block md:px-6"
      >
        <NuxtLink
          :to="pathFor(heroChampion.id)"
          class="text-muted hover:text-default"
        >
          {{ heroChampion.name }}
        </NuxtLink>, first of this patch's most played and banned
      </p>
    </section>

    <!-- The desktop app, announced before the panels it brings into champion
         select. -->
    <HomeDesktopBetaPanel class="pt-16 lg:pt-20" />

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

<style scoped>
/* The splash arrives after the page (both of its sources are client-side), so
   it fades in rather than popping over the text already on screen. */
.hero-splash {
  opacity: 0.45;
  mask-image: linear-gradient(to right, transparent, #000 45%);
  animation: hero-splash-in 600ms ease-out both;
}

@keyframes hero-splash-in {
  from { opacity: 0; }
}

@media (min-width: 768px) {
  .hero-splash { opacity: 0.8; }
}

@media (prefers-reduced-motion: reduce) {
  .hero-splash { animation: none; }
}
</style>
