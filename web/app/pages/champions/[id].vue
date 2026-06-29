<script setup lang="ts">
import type {
  RuneTreeResponse,
  StaticItemData,
  StaticSummonerSpellData,
} from '~~/shared/types/static-data'
import { isChampionPosition, type ChampionPosition } from '~/utils/positions'
import { describeFetchError } from '~/utils/errors'

const route = useRoute()
const championId = computed(() => Number.parseInt(String(route.params.id), 10))

const { filters, setFilter } = useChampionFilters()
const nuxtApp = useNuxtApp()

const {
  data: champion,
  error: championError,
  status: championStatus,
  notEnoughData,
} = useChampion(championId, filters)

// Real load failures (429/500/network) surface as a toast as well as the
// inline alert below — both read the same line via describeFetchError. A 404
// (no data for this champion) is not an error: useChampion swallows it into
// notEnoughData and we render a dedicated empty state instead.
useErrorToast(championError, { title: 'Failed to load champion' })

const activePatch = computed(() => champion.value?.patch || filters.value.patch || null)

const { data: staticData, status: staticStatus } = useChampionStatic(championId, activePatch)
const { data: versions } = useDDragonVersions()

// Winrate/pickrate trend across the last five patches (issues #89, #112).
// Follows the resolved lane so it tracks whatever slice the page is showing,
// but is deliberately cross-patch: the composable forwards only the position,
// never the pinned patch, so the active patch filter never scopes the chart
// and the series always spans recent history. Gated on the champion fetch so
// it fires once with the resolved lane instead of twice (an initial call with
// a null lane, then a refetch the moment the champion's position lands).
const trendReady = computed(() => champion.value !== null)
const trendPosition = computed(() => champion.value?.position || filters.value.position || null)
const { data: championTrend, status: trendStatus } = useChampionTrend(championId, trendPosition, trendReady)

// Share keys with /champions so the patch-keyed maps stay deduped across the
// list→detail→list round-trip. The list page issues these same fetches with
// the same keys; without that alignment Nuxt would re-resolve them on mount.
// Each fetch wraps the network call so `markStaticFetched` runs after success
// and `getCachedData` reuses entries across navigations within
// `STATIC_CACHE_TTL_MS` (see static-cache.ts).
const { data: staticList, status: staticListStatus } = useChampionStaticList()
// Pin rune-tree to the champion's active patch so the icon URLs we render
// hit CommunityDragon's per-patch (year-cacheable) tree, and so cached
// payloads don't bleed across patches when the user navigates between them.
const { data: runeTree, status: runeTreeStatus } = useLazyAsyncData<RuneTreeResponse>(
  () => `rune-tree-${activePatch.value || 'latest'}`,
  async () => {
    const key = `rune-tree-${activePatch.value || 'latest'}`
    const data = await $fetch<RuneTreeResponse>('/api/static/rune-tree', {
      query: activePatch.value ? { patch: activePatch.value } : {},
    })
    markStaticFetched(key, nuxtApp)
    return data
  },
  {
    watch: [activePatch],
    getCachedData: key => getStaticCachedData(key, nuxtApp),
    server: false,
  },
)
const { data: itemsMap, status: itemsStatus } = useLazyAsyncData<Record<number, StaticItemData>>(
  () => `static-items-${activePatch.value || 'latest'}`,
  async () => {
    const key = `static-items-${activePatch.value || 'latest'}`
    const data = await $fetch<Record<number, StaticItemData>>('/api/static/items', {
      query: activePatch.value ? { patch: activePatch.value } : {},
    })
    markStaticFetched(key, nuxtApp)
    return data
  },
  {
    watch: [activePatch],
    getCachedData: key => getStaticCachedData(key, nuxtApp),
    server: false,
  },
)
const { data: summonersMap, status: summonersStatus } = useLazyAsyncData<Record<number, StaticSummonerSpellData>>(
  () => `static-summoners-${activePatch.value || 'latest'}`,
  async () => {
    const key = `static-summoners-${activePatch.value || 'latest'}`
    const data = await $fetch<Record<number, StaticSummonerSpellData>>('/api/static/summoner-spells', {
      query: activePatch.value ? { patch: activePatch.value } : {},
    })
    markStaticFetched(key, nuxtApp)
    return data
  },
  {
    watch: [activePatch],
    getCachedData: key => getStaticCachedData(key, nuxtApp),
    server: false,
  },
)

// Fall back to the list-page entry when the per-champion endpoint is still
// pending or the patch failed to resolve — keeps the header readable instead
// of flashing the numeric id.
const championListEntry = computed(() =>
  (staticList.value ?? []).find(item => item.championId === championId.value) ?? null,
)
const displayName = computed(() =>
  staticData.value?.championName || championListEntry.value?.name || null,
)
const displayIconUrl = computed(() =>
  staticData.value?.championIconUrl || championListEntry.value?.iconUrl || null,
)

useSeoMeta({
  title: () => displayName.value ?? 'TrueMain',
  description: () => `Champion ${championId.value} builds, runes and skill order.`,
})

const patchOptions = computed(() => {
  const seen = new Set<string>(
    (versions.value ?? [])
      .map(p => p.split('.').slice(0, 2).join('.'))
      .filter(Boolean)
      .slice(0, 12),
  )
  if (champion.value?.patch) seen.add(champion.value.patch)
  if (filters.value.patch) seen.add(filters.value.patch)
  return [...seen]
    .map(p => ({ label: p, value: p }))
    .sort((a, b) => b.value.localeCompare(a.value, undefined, { numeric: true }))
})

// Bind to the API-returned patch once available so the picker reflects what's
// actually being shown — covers the 404 fallback in useChampion where the URL
// filter is dropped (no data for the champion on that patch) and the API
// returns its default patch. Mirrors selectedPosition so a no-data patch
// snaps the selector back to the loaded patch instead of leaving the dead
// filter pinned. The URL-filter fallback only applies on the initial load
// (champion.value still null); on later patch swaps champion.value holds the
// previous (stale) data, so the selector keeps showing the old patch until the
// refetch resolves — intentional, and identical to selectedPosition.
const selectedPatch = computed(() => champion.value?.patch || filters.value.patch || '')
// Bind to the API-returned position once available so the picker reflects
// what's actually being shown — covers the 404 fallback in useChampion
// where the URL filter is dropped and the API returns the default position.
// Fall back to the URL filter for the optimistic render before the fetch resolves.
const selectedPosition = computed<ChampionPosition | null>(() => {
  const value = champion.value?.position || filters.value.position || ''
  return isChampionPosition(value) ? value : null
})

// Average per-interval lead vs the lane opponent (issue #525). Follows the
// resolved lane like the trend chart, but is patch-scoped: the active patch
// filter narrows the slice. Gated on the champion fetch so it fires once with
// the resolved lane. Empty until matches are (re-)ingested with snapshots.
const { data: championLeads, status: leadsStatus } = useChampionTimelineLeads(
  championId,
  trendPosition,
  selectedPatch,
  trendReady,
)

// Win rate by game duration (issue #537). Same lane/patch scoping and gating as
// the leads chart; computed from match outcomes, so it has data even before any
// timeline snapshots are ingested.
const { data: championScaling, status: scalingStatus } = useChampionScaling(
  championId,
  trendPosition,
  selectedPatch,
  trendReady,
)

// Average item-purchase times — power spikes (issue #524). Same lane/patch scoping
// and gating; rendered against the page's static item map.
const { data: championItemTimings, status: itemTimingsStatus } = useChampionItemTimings(
  championId,
  trendPosition,
  selectedPatch,
  trendReady,
)

// Roam metric — out-of-lane early kill participations (issue #536). Same lane/patch
// scoping and gating as the other timeline-derived stats.
const { data: championRoam, status: roamStatus } = useChampionRoam(
  championId,
  trendPosition,
  selectedPatch,
  trendReady,
)

// When useChampion's 404 fallback drops the URL filters (no data for the
// champion on that patch/position) the API returns the default slice, but the
// dead patch/position query param lingers in the URL. Once the fetch resolves,
// reconcile the URL with what was actually loaded so a no-data selection snaps
// the address bar back to the initial state instead of pinning a stale filter.
// The watch fires when champion data changes (never on the optimistic
// stale-data phase) and once immediately on mount if champion is already
// populated (e.g. an SSR payload) — so the dead filter is reconciled on the
// first render too, not only on the next change. A *valid* selection — where
// the API echoes the request — never triggers a reset.
watch(champion, (data) => {
  if (!data) return
  // Only reset when the API actually returned a (truthy) value that differs:
  // a missing/empty patch or position in the response means "no slice info",
  // not "your valid filter was dropped", so it must never clear a live filter.
  const updates: { patch?: string | null, position?: ChampionPosition | null } = {}
  if (filters.value.patch && data.patch && filters.value.patch !== data.patch) updates.patch = null
  if (filters.value.position && data.position && filters.value.position !== data.position) updates.position = null
  if (updates.patch !== undefined || updates.position !== undefined) setFilter(updates).catch(console.error)
}, { immediate: true })

// Bound to every async source so the bar covers both the initial lazy load
// and patch/position swaps where the previous champion's data is still on
// screen. `idle` is the pre-fetch state from useLazy* before the client
// kicks them off — treat it as loading too.
const isLoadingStatus = (s: 'idle' | 'pending' | 'success' | 'error') => s === 'idle' || s === 'pending'
const isRefetching = computed(() =>
  isLoadingStatus(championStatus.value)
  || isLoadingStatus(staticStatus.value)
  || isLoadingStatus(staticListStatus.value)
  || isLoadingStatus(runeTreeStatus.value)
  || isLoadingStatus(itemsStatus.value)
  || isLoadingStatus(summonersStatus.value)
  || isLoadingStatus(trendStatus.value)
  || isLoadingStatus(leadsStatus.value)
  || isLoadingStatus(scalingStatus.value)
  || isLoadingStatus(itemTimingsStatus.value)
  || isLoadingStatus(roamStatus.value),
)
</script>

<template>
  <main class="mx-auto max-w-5xl space-y-6 p-4 md:p-6">
    <div class="h-0.5">
      <UProgress
        v-if="isRefetching"
        size="xs"
        color="primary"
        aria-label="Loading champion"
      />
    </div>

    <UAlert
      v-if="championError"
      color="error"
      variant="soft"
      title="Failed to load champion"
      :description="describeFetchError(championError)"
    />

    <!--
      No-data empty state: the API returned 404 for this champion (and, if a
      patch/position was pinned, the unfiltered fallback 404'd too) — we simply
      don't hold any aggregate for them yet (a brand-new champion, or one nobody
      in the dataset has played). This is deliberately distinct from the error
      alert above: a 404 is "no data", not a transient failure to retry.
    -->
    <!--
      Deliberately gated on `notEnoughData` alone, NOT `!isRefetching`: this
      block depends on no secondary data, and `isRefetching` stays true while
      the static fetches (rune tree, items, summoners…) run — which they do even
      for a champion we hold no data on — so anding it in would blank the page
      behind the progress bar until those resolve. The bar already signals that
      background activity.
    -->
    <div
      v-else-if="notEnoughData"
      class="flex flex-col items-center gap-3 glass rounded-lg px-6 py-12 text-center"
    >
      <SkeletonImage
        v-if="displayIconUrl"
        :src="displayIconUrl"
        :alt="displayName ?? ''"
        width="64"
        height="64"
        class="size-16 rounded opacity-80"
      />
      <div class="space-y-1">
        <p class="text-sm font-medium text-default">
          No data yet for {{ displayName ?? 'this champion' }}
        </p>
        <p class="text-sm text-muted">
          We don't have any games on {{ displayName ?? 'this champion' }} yet — once it's
          been played enough, its builds, runes and stats will show up here.
        </p>
      </div>
    </div>

    <template v-else-if="champion && staticData">
      <header class="flex flex-wrap items-center gap-4">
        <ChampionHeader
          :champion-name="displayName"
          :champion-icon-url="displayIconUrl"
          :champion-id="championId"
          :position="champion.position"
          :total-games="champion.totalGames"
          :total-wins="champion.totalWins"
        />
        <ChampionFilters
          :selected-patch="selectedPatch"
          :selected-position="selectedPosition"
          :patch-options="patchOptions"
          @update:patch="value => setFilter({ patch: value })"
          @update:position="value => setFilter({ position: value })"
        />
      </header>

      <ChampionBuildTabs
        :builds="champion.builds"
        :champion-static="staticData"
        :items-map="itemsMap ?? {}"
        :summoners-map="summonersMap ?? {}"
        :rune-tree="runeTree ?? null"
      />

      <ChampionMatchups
        :champion-id="championId"
        :position="selectedPosition"
        :champions="staticList ?? []"
      />

      <ChampionTrendChart
        :points="championTrend?.points ?? []"
        :loading="isLoadingStatus(trendStatus)"
      />

      <div class="grid grid-cols-1 gap-6 lg:grid-cols-2">
        <ChampionTimelineLeadsChart
          :intervals="championLeads?.intervals ?? []"
          :loading="isLoadingStatus(leadsStatus)"
        />

        <ChampionScalingChart
          :buckets="championScaling?.buckets ?? []"
          :scaling-index="championScaling?.scalingIndex ?? null"
          :loading="isLoadingStatus(scalingStatus)"
        />
      </div>

      <ChampionItemTimings
        :timings="championItemTimings?.items ?? []"
        :items-map="itemsMap ?? {}"
        :loading="isLoadingStatus(itemTimingsStatus)"
      />

      <ChampionRoam
        v-if="trendPosition !== 'JUNGLE'"
        :kp5="championRoam?.roamKp5 ?? null"
        :kp10="championRoam?.roamKp10 ?? null"
        :kp15="championRoam?.roamKp15 ?? null"
        :games="championRoam?.games ?? 0"
        :loading="isLoadingStatus(roamStatus)"
      />
    </template>
  </main>
</template>
