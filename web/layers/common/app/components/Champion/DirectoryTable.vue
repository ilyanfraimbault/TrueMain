<script setup lang="ts">
import type { TableColumn } from '@nuxt/ui'
import type { RouteLocationRaw } from 'vue-router'
import type { ChampionSummaryResponse } from '#shared/types/champions'
import type { RuneTreeResponse, StaticItemData } from '#shared/types/static-data'
import { formatPercentage, formatPercentageOrDash } from '#shared/utils/ddragon'
import { formatCount } from '#shared/utils/counts'
import { POSITION_BY_VALUE } from '#common/utils/positions'
import { winRateTone } from '#common/utils/rate-tone'
import { directoryOrderToSorting, parseDirectoryOrder, type DirectoryOrder, type TableSorting } from '#common/utils/table-sorting'
import { clickSelectableRow, wantsNewTab } from '#common/utils/table-rows'

type DirectoryRow = ChampionSummaryResponse & { name: string, iconUrl: string }

// The /champions directory as a `UTable` (#1734): one page of (champion, lane)
// lines, already filtered, ordered and paged by `GET /champions/directory`.
// Tier, win, pick and ban rate and games sort, both ways — a header click
// emits the new order, the page writes it to `?sort=`/`?order=` and the API
// answers with the page in that order (manual sorting: the table never
// reorders rows itself).
//
// The table is an `@container`; a column drops at a width by carrying the same
// `hidden @…:table-cell` on its header and its cells. A line opens the
// champion page on its lane (`@select`, Enter/Space through
// `clickSelectableRow`) — a button-style row, per #147. The cold load renders
// placeholder lines through the same columns, so nothing moves when the data
// lands; a refetch keeps the rows and runs the bar under the header.
const props = defineProps<{
  rows: DirectoryRow[]
  /** Place of the first row in the whole filtered list, for the `#` column across pages. */
  offset: number
  order: DirectoryOrder
  /** Cold load: placeholder lines instead of `rows`. */
  loading: boolean
  /** A refetch is in flight — the bar under the header. */
  refreshing?: boolean
  skeletonRows: number
  destination: (row: DirectoryRow) => RouteLocationRaw
  runeTree: RuneTreeResponse | null | undefined
  itemsMap: Record<number, StaticItemData> | null | undefined
}>()

const emit = defineEmits<{ 'update:order': [order: DirectoryOrder] }>()

// Literal class strings, for Tailwind's scan.
const FROM_XL = 'hidden @xl:table-cell'
const FROM_2XL = 'hidden @2xl:table-cell'
const FROM_4XL = 'hidden @4xl:table-cell'
const FIGURE = 'w-14 text-right @2xl:w-16'

const columns: TableColumn<DirectoryRow>[] = [
  { id: 'place', header: '#', meta: { class: { th: 'w-6 @xl:w-10', td: 'w-6 @xl:w-10' } } },
  { id: 'champion', header: 'Champion', meta: { class: { th: 'w-full max-w-0', td: 'w-full max-w-0' } } },
  { id: 'lane', header: 'Lane', meta: { class: { th: 'w-10 text-center', td: 'w-10' } } },
  { id: 'tier', accessorKey: 'tier', enableSorting: true, meta: { class: { th: 'w-12 text-center', td: 'w-12' } } },
  { id: 'runes', header: 'Runes', meta: { class: { th: `${FROM_2XL} w-12 text-center`, td: `${FROM_2XL} w-12` } } },
  { id: 'build', header: 'Core build', meta: { class: { th: FROM_4XL, td: FROM_4XL } } },
  { id: 'winRate', accessorKey: 'winRate', enableSorting: true, meta: { class: { th: FIGURE, td: FIGURE } } },
  { id: 'pickRate', accessorKey: 'pickRate', enableSorting: true, meta: { class: { th: FIGURE, td: FIGURE } } },
  { id: 'banRate', accessorKey: 'banRate', enableSorting: true, meta: { class: { th: `${FROM_XL} ${FIGURE}`, td: `${FROM_XL} ${FIGURE}` } } },
  { id: 'games', accessorKey: 'games', enableSorting: true, meta: { class: { th: `${FROM_XL} ${FIGURE}`, td: `${FROM_XL} ${FIGURE}` } } },
]

const SORTABLE: { id: string, label: string, title: string, align: 'center' | 'end' }[] = [
  { id: 'tier', label: 'Tier', title: 'tier', align: 'center' },
  { id: 'winRate', label: 'WR', title: 'win rate', align: 'end' },
  { id: 'pickRate', label: 'PR', title: 'pick rate', align: 'end' },
  { id: 'banRate', label: 'BR', title: 'ban rate', align: 'end' },
  { id: 'games', label: 'Games', title: 'games', align: 'end' },
]

const sorting = computed(() => directoryOrderToSorting(props.order))

function onSortingChange(next: TableSorting | undefined) {
  const parsed = parseDirectoryOrder(next?.[0]?.id, next?.[0]?.desc === false ? 'asc' : 'desc')
  if (parsed.sort !== props.order.sort || parsed.order !== props.order.order) emit('update:order', parsed)
}

// A first click sorts a column strongest-first; a click on the column already
// sorting that way flips it.
const toggle = (column: { getIsSorted: () => false | 'asc' | 'desc', toggleSorting: (desc?: boolean) => void }) =>
  column.toggleSorting(column.getIsSorted() !== 'desc')

// Placeholder lines for the cold load: the same columns, a skeleton per cell.
const placeholders = computed(() => Array.from({ length: props.skeletonRows }, (_, index) =>
  ({ championId: -(index + 1), position: 'PLACEHOLDER' }) as DirectoryRow))
const data = computed(() => (props.loading ? placeholders.value : props.rows))

function openChampion(event: Event, row: { original: DirectoryRow }) {
  void navigateTo(props.destination(row.original), wantsNewTab(event) ? { open: { target: '_blank' } } : undefined)
}

const { perk, perkStyle, item } = useBuildResolvers(() => props.runeTree, () => props.itemsMap ?? undefined)

// The consensus path capped at six — the full ADC core, the detail page's worst case.
const buildPathOf = (row: DirectoryRow) => (row.topBuild?.itemPath ?? []).slice(0, 6)
</script>

<template>
  <UTable
    :data="data"
    :columns="columns"
    :sorting="sorting"
    :sorting-options="{ manualSorting: true, enableMultiSort: false, enableSortingRemoval: false, sortDescFirst: true }"
    :get-row-id="(row: DirectoryRow) => `${row.championId}-${row.position}`"
    :watch-options="{ deep: false }"
    :loading="refreshing && !loading"
    :on-select="loading ? undefined : openChampion"
    class="@container"
    @update:sorting="onSortingChange"
    @keydown="clickSelectableRow"
  >
    <template v-for="sortable in SORTABLE" :key="sortable.id" #[`${sortable.id}-header`]="{ column }">
      <TableSortHeader :column="column" :label="sortable.label" :title="sortable.title" :align="sortable.align" @sort="toggle(column)" />
    </template>

    <template #place-cell="{ row }">
      <USkeleton v-if="loading" class="h-4 w-5" />
      <span v-else class="text-xs font-semibold tabular-nums text-muted @xl:text-sm">{{ offset + row.index + 1 }}</span>
    </template>

    <template #champion-cell="{ row }">
      <div class="flex min-w-0 items-center gap-2 @xl:gap-2.5">
        <USkeleton v-if="loading" class="size-7 shrink-0 rounded-md @xl:size-9" />
        <SkeletonImage v-else :src="row.original.iconUrl" :alt="row.original.name" width="36" height="36" class="size-7 shrink-0 rounded-md @xl:size-9" />
        <USkeleton v-if="loading" class="h-4 w-24 max-w-full" />
        <span v-else class="truncate font-semibold text-highlighted">{{ row.original.name }}</span>
      </div>
    </template>

    <template #lane-cell="{ row }">
      <USkeleton v-if="loading" class="mx-auto size-5 rounded" />
      <SkeletonImage
        v-else-if="POSITION_BY_VALUE.get(row.original.position)"
        :src="POSITION_BY_VALUE.get(row.original.position)!.iconUrl"
        transparent
        :alt="POSITION_BY_VALUE.get(row.original.position)!.label"
        :title="POSITION_BY_VALUE.get(row.original.position)!.label"
        :width="20"
        :height="20"
        class="mx-auto size-5"
      />
    </template>

    <template #tier-cell="{ row }">
      <USkeleton v-if="loading" class="mx-auto size-6 rounded-md" />
      <span v-else class="flex justify-center"><TierBadge :tier="row.original.tier" /></span>
    </template>

    <template #runes-cell="{ row }">
      <USkeleton v-if="loading" class="mx-auto size-7 rounded-full" />
      <div v-else-if="row.original.topBuild && perk(row.original.topBuild.primaryKeystoneId)" class="relative mx-auto size-7">
        <GameTooltipPerkIcon :perk="perk(row.original.topBuild.primaryKeystoneId)" :width="28" :height="28" class="size-7 rounded-full" />
        <GameTooltipPerkStyleIcon
          v-if="perkStyle(row.original.topBuild.secondaryStyleId)"
          :style="perkStyle(row.original.topBuild.secondaryStyleId)"
          :width="14"
          :height="14"
          class="absolute -bottom-0.5 -right-1.5 size-3.5"
        />
      </div>
      <span v-else class="block text-center text-dimmed">—</span>
    </template>

    <template #build-cell="{ row }">
      <div v-if="loading" class="flex items-center gap-1.5">
        <USkeleton v-for="slot in 5" :key="slot" class="size-6 rounded" />
      </div>
      <div v-else-if="buildPathOf(row.original).length" class="flex items-center gap-0.5">
        <template v-for="(itemId, index) in buildPathOf(row.original)" :key="`${itemId}-${index}`">
          <GameTooltipItemIcon :item="item(itemId)" :width="24" :height="24" class="size-6 rounded" />
          <UIcon v-if="index < buildPathOf(row.original).length - 1" name="i-lucide-chevron-right" class="size-3 shrink-0 text-dimmed" />
        </template>
      </div>
      <span v-else class="text-dimmed">—</span>
    </template>

    <template #winRate-cell="{ row }">
      <USkeleton v-if="loading" class="ml-auto h-4 w-10" />
      <span v-else class="font-semibold tabular-nums" :class="winRateTone(row.original.winRate)">{{ formatPercentage(row.original.winRate, 0) }}</span>
    </template>
    <template #pickRate-cell="{ row }">
      <USkeleton v-if="loading" class="ml-auto h-4 w-10" />
      <span v-else class="tabular-nums text-default">{{ formatPercentage(row.original.pickRate, 0) }}</span>
    </template>
    <!-- Colour stays on the win rate alone: pick rate reads plain, ban rate and games
         recede. A dash on patches predating ban ingestion (#920): "not observed" is not 0%. -->
    <template #banRate-cell="{ row }">
      <USkeleton v-if="loading" class="ml-auto h-4 w-10" />
      <span v-else class="tabular-nums text-muted">{{ formatPercentageOrDash(row.original.banRate, 0) }}</span>
    </template>
    <template #games-cell="{ row }">
      <USkeleton v-if="loading" class="ml-auto h-4 w-12" />
      <span v-else class="tabular-nums text-muted">{{ formatCount(row.original.games) }}</span>
    </template>
  </UTable>
</template>
