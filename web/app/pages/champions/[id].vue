<script setup lang="ts">
import type {
  ChampionStaticListItem,
  RuneTreeResponse,
  StaticItemData,
  StaticSummonerSpellData,
} from '~~/shared/types/static-data'
import { isChampionPosition, type ChampionPosition } from '~/utils/positions'

const route = useRoute()
const championId = computed(() => Number.parseInt(String(route.params.id), 10))

const { filters, setFilter } = useChampionFilters()
const nuxtApp = useNuxtApp()

const {
  data: champion,
  error: championError,
  status: championStatus,
} = useChampion(championId, filters)

const activePatch = computed(() => champion.value?.patch || filters.value.patch || null)

const { data: staticData, status: staticStatus } = useChampionStatic(championId, activePatch)
const { data: versions } = useDDragonVersions()

// Share keys with /champions so the patch-keyed maps stay deduped across the
// list→detail→list round-trip. The list page issues these same fetches with
// the same keys; without that alignment Nuxt would re-resolve them on mount.
// Each fetch wraps the network call so `markStaticFetched` runs after success
// and `getCachedData` reuses entries across navigations within
// `STATIC_CACHE_TTL_MS` (see static-cache.ts).
const { data: staticList, status: staticListStatus } = useLazyAsyncData<ChampionStaticListItem[]>(
  'champion-static-list',
  async () => {
    const data = await $fetch<ChampionStaticListItem[]>('/api/static/champions')
    markStaticFetched('champion-static-list', nuxtApp)
    return data
  },
  { getCachedData: key => getStaticCachedData(key, nuxtApp), server: false },
)
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

const selectedPatch = computed(() => filters.value.patch || champion.value?.patch || '')
// Bind to the API-returned position once available so the picker reflects
// what's actually being shown — covers the 404 fallback in useChampion
// where the URL filter is dropped and the API returns the default position.
// Fall back to the URL filter for the optimistic render before the fetch resolves.
const selectedPosition = computed<ChampionPosition | null>(() => {
  const value = champion.value?.position || filters.value.position || ''
  return isChampionPosition(value) ? value : null
})

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
  || isLoadingStatus(summonersStatus.value),
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
      :description="championError.message"
    />

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
    </template>
  </main>
</template>
