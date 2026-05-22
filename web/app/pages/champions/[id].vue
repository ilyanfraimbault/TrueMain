<script setup lang="ts">
import type {
  ChampionStaticListItem,
  RuneTreeResponse,
  StaticItemData,
  StaticSummonerSpellData,
} from '~~/shared/types/static-data'
import { POSITION_OPTIONS, type ChampionPosition } from '~/utils/positions'

const route = useRoute()
const championId = computed(() => Number.parseInt(String(route.params.id), 10))

const { filters, setFilter } = useChampionFilters()
const nuxtApp = useNuxtApp()

const {
  data: champion,
  error: championError,
  status: championStatus,
} = await useChampion(championId, filters)

const activePatch = computed(() => champion.value?.patch || filters.value.patch || null)

const { data: staticData, status: staticStatus } = await useChampionStatic(championId, activePatch)
const { data: versions } = useDDragonVersions()

// Share keys with /champions so the patch-keyed maps stay deduped across the
// list→detail→list round-trip. The list page issues these same fetches with
// the same keys; without that alignment Nuxt would re-resolve them on mount.
// Each fetch wraps the network call so `markStaticFetched` runs after success
// and `getCachedData` reuses entries across navigations within
// `STATIC_CACHE_TTL_MS` (see static-cache.ts).
const { data: staticList, status: staticListStatus } = await useAsyncData<ChampionStaticListItem[]>(
  'champion-static-list',
  async () => {
    const data = await $fetch<ChampionStaticListItem[]>('/api/static/champions')
    markStaticFetched('champion-static-list', nuxtApp)
    return data
  },
  { getCachedData: key => getStaticCachedData(key, nuxtApp) },
)
const { data: runeTree, status: runeTreeStatus } = await useAsyncData<RuneTreeResponse>(
  'rune-tree',
  async () => {
    const data = await $fetch<RuneTreeResponse>('/api/static/rune-tree')
    markStaticFetched('rune-tree', nuxtApp)
    return data
  },
  { getCachedData: key => getStaticCachedData(key, nuxtApp) },
)
const { data: itemsMap, status: itemsStatus } = await useAsyncData<Record<number, StaticItemData>>(
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
  },
)
const { data: summonersMap, status: summonersStatus } = await useAsyncData<Record<number, StaticSummonerSpellData>>(
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
const selectedPosition = computed<ChampionPosition | ''>(() => {
  const value = filters.value.position || champion.value?.position || ''
  return POSITION_OPTIONS.some(o => o.value === value) ? value as ChampionPosition : ''
})

const isLoading = computed(() => championStatus.value === 'pending' && !champion.value)
// Bound to every async source on the page so patch swaps surface a cue even
// when the previous champion's data is still rendered.
const isRefetching = computed(() =>
  championStatus.value === 'pending'
  || staticStatus.value === 'pending'
  || staticListStatus.value === 'pending'
  || runeTreeStatus.value === 'pending'
  || itemsStatus.value === 'pending'
  || summonersStatus.value === 'pending',
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

    <p
      v-if="isLoading"
      class="text-sm"
    >
      Loading…
    </p>

    <UAlert
      v-else-if="championError"
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
          :position-options="POSITION_OPTIONS"
          @update:patch="value => setFilter(value, null)"
          @update:position="value => setFilter(null, value)"
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
