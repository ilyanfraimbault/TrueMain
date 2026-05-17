<script setup lang="ts">
import { h, resolveComponent } from 'vue'
import type { Column } from '@tanstack/vue-table'
import type { TableColumn, TableRow } from '@nuxt/ui'
import type { ChampionSummaryResponse } from '~~/shared/types/champions'
import type { ChampionStaticListItem } from '~~/shared/types/static-data'
import { formatPercentage } from '~~/shared/utils/ddragon'

useSeoMeta({
  title: 'Champions · TrueMain',
  description: 'Browse every champion with games played and win rate on the latest patch.',
})

const router = useRouter()
const UButton = resolveComponent('UButton')

const [summariesState, staticState] = await Promise.all([
  useFetch<ChampionSummaryResponse[]>('/api/champions', { key: 'champions-list' }),
  useFetch<ChampionStaticListItem[]>('/api/static/champions', { key: 'champion-static-list' }),
])

const summaries = summariesState.data
const staticList = staticState.data
const error = computed(() => summariesState.error.value ?? staticState.error.value)

type Row = {
  championId: number
  name: string
  iconUrl: string
  games: number
  winRate: number
  position: string
}

const rows = computed<Row[]>(() => {
  const nameById = new Map(
    (staticList.value ?? []).map(item => [item.championId, item]),
  )
  return (summaries.value ?? []).map(summary => ({
    championId: summary.championId,
    name: nameById.get(summary.championId)?.name ?? `Champion ${summary.championId}`,
    iconUrl: nameById.get(summary.championId)?.iconUrl ?? '',
    games: summary.games,
    winRate: summary.winRate,
    position: summary.position,
  }))
})

const sorting = ref([{ id: 'games', desc: true }])

function sortableHeader(label: string, align: 'left' | 'right' = 'left') {
  return ({ column }: { column: Column<Row> }) => {
    const direction = column.getIsSorted()
    const icon = direction === 'asc'
      ? 'i-lucide-arrow-up'
      : direction === 'desc'
        ? 'i-lucide-arrow-down'
        : 'i-lucide-arrow-up-down'
    return h(UButton, {
      label,
      icon,
      color: 'neutral',
      variant: 'ghost',
      size: 'sm',
      class: align === 'right' ? '-mr-2 ml-auto' : '-ml-2',
      trailingIcon: undefined,
      onClick: () => column.toggleSorting(direction === 'asc'),
    })
  }
}

const columns: TableColumn<Row>[] = [
  { accessorKey: 'name', header: sortableHeader('Champion') },
  { accessorKey: 'position', header: sortableHeader('Position') },
  {
    accessorKey: 'games',
    header: sortableHeader('Games', 'right'),
    meta: { class: { th: 'text-right', td: 'text-right tabular-nums' } },
  },
  {
    accessorKey: 'winRate',
    header: sortableHeader('Win rate', 'right'),
    meta: { class: { th: 'text-right', td: 'text-right tabular-nums' } },
  },
]

function onSelect(_event: Event, row: TableRow<Row>) {
  router.push(`/champions/${row.original.championId}`)
}
</script>

<template>
  <main class="mx-auto max-w-5xl space-y-6 p-4 md:p-6">
    <header class="space-y-1">
      <h1 class="text-2xl font-semibold">
        Champions
      </h1>
      <p class="text-sm text-muted">
        Sorted by games played on each champion's latest patch.
      </p>
    </header>

    <UAlert
      v-if="error"
      color="error"
      variant="soft"
      title="Failed to load champions"
      :description="error.message"
    />

    <UTable
      v-else
      v-model:sorting="sorting"
      :data="rows"
      :columns="columns"
      :loading="summariesState.status.value === 'pending'"
      loading-color="primary"
      :meta="{ class: { tr: 'cursor-pointer' } }"
      @select="onSelect"
    >
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

      <template #position-cell="{ row }">
        <span class="text-muted">{{ row.original.position || '—' }}</span>
      </template>

      <template #winRate-cell="{ row }">
        {{ formatPercentage(row.original.winRate) }}
      </template>
    </UTable>
  </main>
</template>
