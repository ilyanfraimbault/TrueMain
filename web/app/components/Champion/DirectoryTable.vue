<script setup lang="ts">
import type { RouteLocationRaw } from 'vue-router'
import type { ChampionSummaryResponse } from '~~/shared/types/champions'
import type { RuneTreeResponse, StaticItemData } from '~~/shared/types/static-data'
import { CHAMPIONS_TABLE_GRID } from '~/utils/list-tables'

type DirectoryRow = ChampionSummaryResponse & { name: string, iconUrl: string }

// The /champions directory as a table (#1726): one header of column labels
// over (champion, lane) rows laid on the same grid (`CHAMPIONS_TABLE_GRID`) —
// the desktop app's champion table. The table is the `@container` the grid's
// tiers read; the skeleton rows take the same grid so the data swaps in
// without moving a column.
defineProps<{
  rows: DirectoryRow[]
  /** Place of the first row in the whole filtered list, for the `#` column across pages. */
  offset: number
  /** Cold load: placeholder rows instead of `rows`. */
  loading: boolean
  skeletonRows: number
  destination: (row: DirectoryRow) => RouteLocationRaw
  runeTree: RuneTreeResponse | null | undefined
  itemsMap: Record<number, StaticItemData> | null | undefined
}>()
</script>

<template>
  <div class="surface @container overflow-hidden rounded-xl">
    <div
      class="grid items-center gap-2 border-b border-default px-3 py-2"
      :class="CHAMPIONS_TABLE_GRID"
    >
      <span class="stat-label">#</span>
      <span class="stat-label">Champion</span>
      <span class="stat-label text-center">Lane</span>
      <span class="stat-label text-center">Tier</span>
      <span class="stat-label hidden text-center @2xl:block">Runes</span>
      <span class="stat-label hidden @4xl:block">Core build</span>
      <span class="stat-label text-right" title="Win rate">WR</span>
      <span class="stat-label text-right" title="Pick rate">PR</span>
      <span class="stat-label hidden text-right @xl:block" title="Ban rate">BR</span>
      <span class="stat-label hidden text-right @xl:block">Games</span>
    </div>

    <div v-if="loading" aria-hidden="true">
      <div
        v-for="index in skeletonRows"
        :key="`skeleton-${index}`"
        class="grid h-12 items-center gap-2 border-b border-default/60 px-3 last:border-b-0"
        :class="CHAMPIONS_TABLE_GRID"
      >
        <USkeleton class="h-4 w-5" />
        <div class="flex min-w-0 items-center gap-2 @xl:gap-2.5">
          <USkeleton class="size-7 shrink-0 rounded @xl:size-9" />
          <USkeleton class="h-4 w-24 max-w-full" />
        </div>
        <USkeleton class="mx-auto size-5 rounded" />
        <USkeleton class="mx-auto h-6 w-6 rounded-md" />
        <USkeleton class="mx-auto hidden size-7 rounded-full @2xl:block" />
        <div class="hidden items-center gap-1.5 @4xl:flex">
          <USkeleton v-for="slot in 5" :key="slot" class="size-6 rounded" />
        </div>
        <USkeleton class="ml-auto h-4 w-9" />
        <USkeleton class="ml-auto h-4 w-9" />
        <USkeleton class="ml-auto hidden h-4 w-9 @xl:block" />
        <USkeleton class="ml-auto hidden h-4 w-10 @xl:block" />
      </div>
    </div>

    <template v-else>
      <ChampionDirectoryTableRow
        v-for="(row, index) in rows"
        :key="`${row.championId}-${row.position}`"
        :row="row"
        :rank="offset + index + 1"
        :destination="destination(row)"
        :rune-tree="runeTree"
        :items-map="itemsMap"
      />
    </template>
  </div>
</template>
