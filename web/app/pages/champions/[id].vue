<script setup lang="ts">
import type { RuneTreeResponse } from '~~/shared/types/static-data'
import { POSITION_OPTIONS, type ChampionPosition } from '~/utils/positions'

const route = useRoute()
const championId = computed(() => Number.parseInt(String(route.params.id), 10))

const { filters, setFilter } = useChampionFilters()

const {
  data: champion,
  error: championError,
  status: championStatus,
} = await useChampion(championId, filters)

const activePatch = computed(() => champion.value?.patch || filters.value.patch || null)

const { data: staticData } = await useChampionStatic(championId, activePatch)
const { data: versions } = useDDragonVersions()
const { data: runeTree } = await useFetch<RuneTreeResponse>('/api/static/rune-tree', { key: 'rune-tree' })

useSeoMeta({
  title: () => staticData.value?.championName ?? 'TrueMain',
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
</script>

<template>
  <main class="mx-auto max-w-5xl space-y-6 p-4 md:p-6">
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
          :champion-static="staticData"
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
        :rune-tree="runeTree ?? null"
      />
    </template>
  </main>
</template>
