<script setup lang="ts">
import type { RegionSlug } from '~~/shared/types/leaderboard'
import type { ChampionPosition } from '~/utils/positions'

useSeoMeta({
  title: 'OTP Leaderboard',
  description: 'Leaderboard of tracked one-trick (OTP) League of Legends players, ranked live and filterable by region, role and champion.',
})

useSchemaOrg([
  defineWebPage({ name: 'OTP Leaderboard', description: 'Tracked one-trick League of Legends players ranked by rank.' }),
])

const LEADERBOARD_PAGE_SIZE = 25
const VALID_REGIONS: ReadonlySet<RegionSlug> = new Set(['europe', 'americas', 'korea'])

const route = useRoute()

// URL state — shared coercion helpers with champions/index.vue + the matches
// feed: invalid values fall back to "no filter" / page 1 instead of
// reflecting attacker-controlled query params back through the API.
const { currentPage, setPage } = useRoutePage()

const filterRegion = computed<RegionSlug | null>(() => {
  const raw = Array.isArray(route.query.region) ? route.query.region[0] : route.query.region
  return typeof raw === 'string' && VALID_REGIONS.has(raw as RegionSlug) ? (raw as RegionSlug) : null
})

const filterPosition = useRouteQueryPosition()
const filterChampionId = useRouteQueryChampionId()

// OTP-only toggle — a truthy `?otpOnly=` narrows the list to one-trick ponies.
// Read tolerantly (`true`/`1`) so a hand-typed or legacy link still resolves,
// but only ever written back as the canonical `true`.
const filterOtpOnly = computed<boolean>(() => {
  const raw = Array.isArray(route.query.otpOnly) ? route.query.otpOnly[0] : route.query.otpOnly
  return raw === 'true' || raw === '1'
})

// Any filter change drops `?page=` — staying on page 5 after narrowing the
// filter set risks landing past the new total (see useRouteFilterSetter).
const setQueryFilter = useRouteFilterSetter()

async function setRegion(next: RegionSlug | null) {
  await setQueryFilter('region', next)
}

async function setPosition(next: ChampionPosition | null) {
  await setQueryFilter('position', next)
}

async function setChampionId(next: number | null) {
  await setQueryFilter('championId', next ? String(next) : null)
}

async function setOtpOnly(next: boolean) {
  // Drop the param entirely when off so the default (all truemains) has a clean
  // URL and shares the unfiltered cache key.
  await setQueryFilter('otpOnly', next ? 'true' : null)
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
  otpOnly: filterOtpOnly,
})

// ─── Static lookups ───────────────────────────────────────────────────────
// The champion list backs the row's top-3 icon lookup and the header's
// unified search — one shared `champion-static-list` cache (warmed by the
// prefetch plugin), so there's no duplicate /api/static/champions request.
const { data: champions } = useChampionStaticList()

const { data: versions } = useDDragonVersions()
const latestPatch = computed(() => versions.value?.[0] ?? null)

// Rune tree + item icons for each row's main-champion build.
const { runeTree, itemsMap } = useBuildAssets(latestPatch)

// Map keyed lookup for the row's top-3 — avoids a linear scan per icon.
const championsById = useChampionsById(champions)
</script>

<template>
  <main class="mx-auto w-full max-w-6xl space-y-6 p-4 md:p-6">
    <PageHeader
      eyebrow="Leaderboard"
      title="Truemains"
      description="Tracked players ranked by current LP. Higher tier wins below Master; Master+ are ordered by raw LP."
    />

    <AppSearch
      variant="field"
      champion-mode="filter"
      :active-champion-id="filterChampionId"
      placeholder="Search a champion or player…"
      @filter-champion="setChampionId"
    />

    <LeaderboardFilters
      :region="filterRegion"
      :position="filterPosition"
      :otp-only="filterOtpOnly"
      @update:region="setRegion"
      @update:position="setPosition"
      @update:otp-only="setOtpOnly"
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
