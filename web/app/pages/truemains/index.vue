<script setup lang="ts">
import type { ChampionStaticListItem } from '~~/shared/types/static-data'
import type { RegionSlug } from '~~/shared/types/leaderboard'
import { isChampionPosition, type ChampionPosition } from '~/utils/positions'

useSeoMeta({
  title: 'Truemains · TrueMain',
  description: 'Tracked truemains sorted by current rank, filterable by region, role and champion.',
})

const LEADERBOARD_PAGE_SIZE = 25
const VALID_REGIONS: ReadonlySet<RegionSlug> = new Set(['europe', 'americas', 'korea'])

const route = useRoute()
const router = useRouter()
const nuxtApp = useNuxtApp()

// URL state — same coercion pattern as champions/index.vue + the matches
// feed: invalid values fall back to "no filter" / page 1 instead of
// reflecting attacker-controlled query params back through the API.
const currentPage = computed<number>(() => {
  const raw = Array.isArray(route.query.page) ? route.query.page[0] : route.query.page
  const parsed = Number.parseInt(typeof raw === 'string' ? raw : '', 10)
  return Number.isFinite(parsed) && parsed >= 1 ? parsed : 1
})

const filterRegion = computed<RegionSlug | null>(() => {
  const raw = Array.isArray(route.query.region) ? route.query.region[0] : route.query.region
  return typeof raw === 'string' && VALID_REGIONS.has(raw as RegionSlug) ? (raw as RegionSlug) : null
})

const filterPosition = computed<ChampionPosition | null>(() => {
  const raw = Array.isArray(route.query.position) ? route.query.position[0] : route.query.position
  return isChampionPosition(raw) ? raw : null
})

const filterChampionId = computed<number | null>(() => {
  const raw = Array.isArray(route.query.championId) ? route.query.championId[0] : route.query.championId
  const parsed = Number.parseInt(typeof raw === 'string' ? raw : '', 10)
  return Number.isFinite(parsed) && parsed > 0 ? parsed : null
})

async function setPage(next: number) {
  const clamped = Math.max(1, Math.floor(next))
  if (clamped === currentPage.value) return
  const nextQuery = { ...route.query }
  if (clamped === 1) delete nextQuery.page
  else nextQuery.page = String(clamped)
  await router.replace({ query: nextQuery })
}

// Any filter change drops `?page=` — staying on page 5 after narrowing the
// filter set risks landing past the new total. Matches the pattern in
// pages/champions/index.vue (`applyFilterReset`).
async function setRegion(next: RegionSlug | null) {
  const nextQuery = { ...route.query }
  if (next) nextQuery.region = next
  else delete nextQuery.region
  delete nextQuery.page
  await router.replace({ query: nextQuery })
}

async function setPosition(next: ChampionPosition | null) {
  const nextQuery = { ...route.query }
  if (next) nextQuery.position = next
  else delete nextQuery.position
  delete nextQuery.page
  await router.replace({ query: nextQuery })
}

async function setChampionId(next: number | null) {
  const nextQuery = { ...route.query }
  if (next) nextQuery.championId = String(next)
  else delete nextQuery.championId
  delete nextQuery.page
  await router.replace({ query: nextQuery })
}

// ─── Leaderboard fetch ────────────────────────────────────────────────────
const {
  rows,
  total,
  pageSize,
  isInitialLoading: leaderboardInitialLoading,
  isLoading: leaderboardLoading,
  error: leaderboardError,
} = useTruemainsLeaderboard(currentPage, {
  pageSize: LEADERBOARD_PAGE_SIZE,
  region: filterRegion,
  position: filterPosition,
  championId: filterChampionId,
})

// ─── Static lookups ───────────────────────────────────────────────────────
// The champion list backs both the picker (championId search) and the row's
// top-3 icon lookup. Cached across navigations via getCachedData.
const { data: champions } = useLazyAsyncData<ChampionStaticListItem[]>(
  'leaderboard-champions',
  async () => {
    const data = await $fetch<ChampionStaticListItem[]>('/api/static/champions')
    markStaticFetched('leaderboard-champions', nuxtApp)
    return data
  },
  {
    default: () => [],
    server: false,
    getCachedData: key => getStaticCachedData(key, nuxtApp),
  },
)

const { data: versions } = useDDragonVersions()
const latestPatch = computed(() => versions.value?.[0] ?? null)

// Rune tree + item icons for each row's main-champion build.
const { runeTree, itemsMap } = useBuildAssets(latestPatch)

// Map keyed lookup for the row's top-3 — avoids a linear scan per icon.
const championsById = computed(() => {
  const map = new Map<number, ChampionStaticListItem>()
  for (const c of champions.value) map.set(c.championId, c)
  return map
})
</script>

<template>
  <main class="mx-auto w-full max-w-6xl space-y-6 p-4 md:p-6">
    <header class="space-y-1">
      <h1 class="text-2xl font-bold text-default">
        Truemains
      </h1>
      <p class="text-sm text-muted">
        Tracked players ranked by current LP. Higher tier wins below Master; Master+ are ordered by raw LP.
      </p>
    </header>

    <TruemainSearch variant="field" />

    <LeaderboardFilters
      :champions="champions"
      :region="filterRegion"
      :position="filterPosition"
      :champion-id="filterChampionId"
      @update:region="setRegion"
      @update:position="setPosition"
      @update:champion-id="setChampionId"
    />

    <ClientOnly>
      <UProgress
        v-if="leaderboardLoading && !leaderboardInitialLoading"
        size="xs"
        color="primary"
        :indeterminate="true"
      />
    </ClientOnly>

    <UAlert
      v-if="leaderboardError"
      color="error"
      icon="i-lucide-alert-triangle"
      title="Couldn't load the leaderboard"
      description="The API request failed. Try refreshing — if it keeps failing, the backend may be down."
    />

    <div v-if="leaderboardInitialLoading" class="space-y-2">
      <LeaderboardRowSkeleton v-for="i in LEADERBOARD_PAGE_SIZE" :key="`skel-${i}`" />
    </div>

    <div v-else-if="rows.length === 0 && !leaderboardError" class="glass rounded-md px-4 py-8 text-center text-sm text-muted">
      No truemains match these filters yet.
    </div>

    <div v-else class="space-y-2">
      <LeaderboardRow
        v-for="row in rows"
        :key="row.rank"
        :row="row"
        :champions-by-id="championsById"
        :rune-tree="runeTree"
        :items-map="itemsMap"
        :patch="latestPatch"
      />
    </div>

    <div v-if="!leaderboardInitialLoading && total > pageSize" class="flex justify-center pt-2">
      <UPagination
        :page="currentPage"
        :total="total"
        :items-per-page="pageSize"
        :sibling-count="1"
        color="neutral"
        variant="ghost"
        active-color="primary"
        active-variant="soft"
        @update:page="setPage"
      />
    </div>
  </main>
</template>
