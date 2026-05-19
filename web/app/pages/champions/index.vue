<script setup lang="ts">
import { h, resolveComponent } from 'vue'
import type { TableColumn, TableRow } from '@nuxt/ui'
import type { ChampionSummaryResponse } from '~~/shared/types/champions'
import type { ChampionStaticListItem } from '~~/shared/types/static-data'
import { formatPercentage } from '~~/shared/utils/ddragon'
import { POSITION_OPTIONS, isChampionPosition, type ChampionPosition } from '~/utils/positions'

useSeoMeta({
  title: 'Champions · TrueMain',
  description: 'Browse champions by lane with pickrate, winrate and games for the selected patch.',
})

const route = useRoute()
const router = useRouter()
const UButton = resolveComponent('UButton')

const { filters, setFilter } = useChampionFilters()

// One asyncData entry per resolved patch — when the patch filter changes,
// `watch` refires and the new entry is cached under a fresh key.
const {
  data: summaries,
  error: summariesError,
  status: summariesStatus,
} = await useAsyncData<ChampionSummaryResponse[]>(
  () => `champions-list-${filters.value.patch ?? 'latest'}`,
  () => {
    const patch = filters.value.patch
    return $fetch<ChampionSummaryResponse[]>('/api/champions', {
      query: patch ? { patch } : {},
    })
  },
  { watch: [filters] },
)
const { data: staticList, error: staticError } = await useFetch<ChampionStaticListItem[]>(
  '/api/static/champions',
  { key: 'champion-static-list' },
)
const { data: versions } = useDDragonVersions()

const error = computed(() => summariesError.value ?? staticError.value)
const isPending = computed(() => summariesStatus.value === 'pending')

// Patch the API actually resolved for these rows (== filters.patch when one is
// pinned, else the global latest). Drives the dropdown selection + ensures the
// resolved patch is always present in the option list.
const apiPatch = computed(() => summaries.value?.[0]?.patchVersion ?? '')

const patchOptions = computed(() => {
  const seen = new Set<string>(
    (versions.value ?? [])
      .map(p => p.split('.').slice(0, 2).join('.'))
      .filter(Boolean)
      .slice(0, 12),
  )
  if (apiPatch.value) seen.add(apiPatch.value)
  if (filters.value.patch) seen.add(filters.value.patch)
  return [...seen]
    .map(p => ({ label: p, value: p }))
    .sort((a, b) => b.value.localeCompare(a.value, undefined, { numeric: true }))
})

const selectedPatch = computed(() => filters.value.patch || apiPatch.value || '')
const selectedPosition = computed<ChampionPosition | typeof ALL_POSITIONS>(() => {
  const value = filters.value.position ?? ''
  return isChampionPosition(value) ? value : ALL_POSITIONS
})

// Reka UI (USelect's underlying primitive) forbids '' as an item value, so the
// "clear" entry uses a sentinel string and gets translated to a URL strip below.
const ALL_POSITIONS = 'all' as const
const POSITION_FILTER_OPTIONS = [
  { label: 'All positions', value: ALL_POSITIONS },
  ...POSITION_OPTIONS.map(o => ({ label: o.label, value: o.value as string })),
]

function onPatchChange(value: unknown) {
  if (typeof value !== 'string' || !value) return
  setFilter(value, null)
}

async function onPositionChange(value: unknown) {
  if (value === ALL_POSITIONS) {
    // useChampionFilters.setFilter() can only add, not clear, so strip the
    // position param via router directly to keep the URL clean.
    const { position: _omit, ...rest } = route.query
    await router.replace({ query: rest })
    return
  }
  if (!isChampionPosition(value)) return
  await setFilter(null, value)
}

const searchQuery = ref('')

type Row = {
  championId: number
  name: string
  iconUrl: string
  position: string
  games: number
  winRate: number
  pickRate: number
  lanePlayRate: number
}

type RankedRow = Row & { rank: number }
type SortKey = 'name' | 'games' | 'winRate' | 'pickRate' | 'lanePlayRate'
const sortState = ref<{ id: SortKey, desc: boolean }>({ id: 'pickRate', desc: true })

function toggleSort(id: SortKey) {
  if (sortState.value.id === id) {
    sortState.value = { id, desc: !sortState.value.desc }
  } else {
    // Numeric columns open descending (best-first); name opens ascending (A→Z).
    sortState.value = { id, desc: id !== 'name' }
  }
}

const baseRows = computed<Row[]>(() => {
  const nameById = new Map(
    (staticList.value ?? []).map(item => [item.championId, item]),
  )
  return (summaries.value ?? []).map(summary => ({
    championId: summary.championId,
    name: nameById.get(summary.championId)?.name ?? `Champion ${summary.championId}`,
    iconUrl: nameById.get(summary.championId)?.iconUrl ?? '',
    position: summary.position,
    games: summary.games,
    winRate: summary.winRate,
    pickRate: summary.pickRate,
    lanePlayRate: summary.lanePlayRate,
  }))
})

const filteredRows = computed(() => {
  let rows = baseRows.value
  const pos = selectedPosition.value
  // selectedPosition holds the ALL_POSITIONS sentinel when the filter is off —
  // only narrow by position when the value is a real ChampionPosition.
  if (pos !== ALL_POSITIONS) rows = rows.filter(r => r.position === pos)
  const q = searchQuery.value.trim().toLowerCase()
  if (q) rows = rows.filter(r => r.name.toLowerCase().includes(q))
  return rows
})

const sortedRows = computed<RankedRow[]>(() => {
  const { id, desc } = sortState.value
  const sorted = [...filteredRows.value].sort((a, b) => {
    const av = a[id]
    const bv = b[id]
    const cmp = typeof av === 'string' && typeof bv === 'string'
      ? av.localeCompare(bv)
      : av < bv ? -1 : av > bv ? 1 : 0
    return desc ? -cmp : cmp
  })
  return sorted.map((row, index) => ({ ...row, rank: index + 1 }))
})

const positionIconUrlMap = new Map(POSITION_OPTIONS.map(o => [o.value as string, o.iconUrl]))

function sortableHeader(label: string, key: SortKey, align: 'left' | 'right' = 'left') {
  return () => {
    const isActive = sortState.value.id === key
    const icon = !isActive
      ? 'i-lucide-arrow-up-down'
      : sortState.value.desc ? 'i-lucide-arrow-down' : 'i-lucide-arrow-up'
    return h(UButton, {
      label,
      icon,
      color: 'neutral',
      variant: 'ghost',
      size: 'sm',
      class: align === 'right' ? '-mr-2 ml-auto' : '-ml-2',
      onClick: () => toggleSort(key),
    })
  }
}

const columns: TableColumn<RankedRow>[] = [
  {
    id: 'rank',
    header: '#',
    meta: { class: { th: 'w-12 text-right text-muted', td: 'w-12 text-right text-muted tabular-nums' } },
  },
  { accessorKey: 'name', header: sortableHeader('Champion', 'name') },
  {
    id: 'lane',
    header: 'Lane',
    meta: { class: { th: 'w-32' } },
  },
  {
    accessorKey: 'winRate',
    header: sortableHeader('Win rate', 'winRate', 'right'),
    meta: { class: { th: 'text-right', td: 'text-right tabular-nums' } },
  },
  {
    accessorKey: 'pickRate',
    header: sortableHeader('Pickrate', 'pickRate', 'right'),
    meta: { class: { th: 'text-right', td: 'text-right tabular-nums' } },
  },
  {
    accessorKey: 'games',
    header: sortableHeader('Games', 'games', 'right'),
    meta: { class: { th: 'text-right', td: 'text-right tabular-nums' } },
  },
]

function onSelect(_event: Event, row: TableRow<RankedRow>) {
  router.push({
    path: `/champions/${row.original.championId}`,
    query: {
      ...(selectedPatch.value ? { patch: selectedPatch.value } : {}),
      ...(row.original.position ? { position: row.original.position } : {}),
    },
  })
}
</script>

<template>
  <main class="mx-auto max-w-6xl space-y-6 p-4 md:p-6">
    <header class="space-y-3">
      <div class="space-y-1">
        <h1 class="text-2xl font-semibold">
          Champions
        </h1>
        <p class="text-sm text-muted">
          One row per champion and lane. Filter by patch or position, or search by name.
        </p>
      </div>
      <div class="flex flex-wrap items-center gap-2">
        <USelect
          :model-value="selectedPatch || undefined"
          :items="patchOptions"
          placeholder="Patch"
          class="w-28"
          @update:model-value="onPatchChange"
        />
        <USelect
          :model-value="selectedPosition"
          :items="POSITION_FILTER_OPTIONS"
          class="w-40"
          @update:model-value="onPositionChange"
        />
        <UInput
          v-model="searchQuery"
          icon="i-lucide-search"
          placeholder="Search champion…"
          class="w-56"
        />
      </div>
    </header>

    <UAlert
      v-if="error"
      color="error"
      variant="soft"
      title="Failed to load champions"
      :description="error.message"
    />

    <template v-else>
      <UTable
        :data="sortedRows"
        :columns="columns"
        :loading="isPending"
        loading-color="primary"
        :meta="{ class: { tr: 'cursor-pointer' } }"
        @select="onSelect"
      >
        <template #rank-cell="{ row }">
          {{ row.original.rank }}
        </template>

        <template #name-cell="{ row }">
          <div class="flex items-center gap-3">
            <SkeletonImage
              :src="row.original.iconUrl"
              :alt="row.original.name"
              width="32"
              height="32"
              class="size-8 rounded"
            />
            <span class="font-medium">{{ row.original.name }}</span>
          </div>
        </template>

        <template #lane-cell="{ row }">
          <div class="flex items-center gap-2">
            <SkeletonImage
              v-if="positionIconUrlMap.get(row.original.position)"
              :src="positionIconUrlMap.get(row.original.position)!"
              :alt="row.original.position"
              width="20"
              height="20"
              class="size-5"
            />
            <span class="text-xs text-muted tabular-nums">
              {{ formatPercentage(row.original.lanePlayRate) }}
            </span>
          </div>
        </template>

        <template #winRate-cell="{ row }">
          {{ formatPercentage(row.original.winRate) }}
        </template>

        <template #pickRate-cell="{ row }">
          {{ formatPercentage(row.original.pickRate) }}
        </template>

        <template #games-cell="{ row }">
          {{ row.original.games.toLocaleString() }}
        </template>
      </UTable>

      <p
        v-if="!isPending && sortedRows.length === 0"
        class="text-sm text-muted"
      >
        No champions match these filters.
      </p>
    </template>
  </main>
</template>
