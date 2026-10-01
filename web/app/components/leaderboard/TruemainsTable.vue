<script setup lang="ts">
import type { LeaderboardRowResponse, LeaderboardSort } from '~~/shared/types/leaderboard'
import type { ChampionStaticListItem, RuneTreeResponse, StaticItemData } from '~~/shared/types/static-data'
import { TRUEMAINS_TABLE_GRID } from '~/utils/list-tables'

// The /truemains leaderboard as a table (#1726): one header of column labels
// over rows laid on the same grid (`TRUEMAINS_TABLE_GRID`), so every figure
// sits under its label — the desktop app's table. The table is the
// `@container` the grid's tiers read, header and rows alike; the skeleton
// rows take the same grid so nothing moves when the data lands.
const props = defineProps<{
  rows: LeaderboardRowResponse[]
  /** Cold load: placeholder rows instead of `rows`. */
  loading: boolean
  skeletonRows: number
  sort: LeaderboardSort
  championsById: Map<number, ChampionStaticListItem>
  runeTree: RuneTreeResponse | null
  itemsMap: Record<number, StaticItemData>
  patch: string | null
}>()

// The column the board is ordered by reads a step brighter than the others.
const sortedClass = (column: LeaderboardSort) => (props.sort === column ? 'text-highlighted' : undefined)
</script>

<template>
  <div class="surface @container overflow-hidden rounded-xl">
    <div
      class="grid items-center gap-2 border-b border-default px-3 py-2"
      :class="TRUEMAINS_TABLE_GRID"
    >
      <span class="stat-label">#</span>
      <span class="stat-label">Player</span>
      <span class="stat-label hidden text-center @2xl:block">Lanes</span>
      <span class="stat-label text-center">Main</span>
      <span class="stat-label text-right" :class="sortedClass('dedication')" title="Truemain score">Score</span>
      <span class="stat-label text-center" :class="sortedClass('rank')">Rank</span>
      <span class="stat-label hidden text-right @xl:block">Games</span>
      <span class="stat-label hidden text-right @xl:block">KDA</span>
      <span class="stat-label hidden text-right @xl:block" title="Win rate">WR</span>
      <span />
    </div>

    <div v-if="loading" aria-hidden="true">
      <div
        v-for="index in skeletonRows"
        :key="`skeleton-${index}`"
        class="grid h-12 items-center gap-2 border-b border-default/60 px-3 last:border-b-0"
        :class="TRUEMAINS_TABLE_GRID"
      >
        <USkeleton class="h-4 w-5" />
        <div class="flex min-w-0 items-center gap-2 @xl:gap-2.5">
          <USkeleton class="size-7 shrink-0 rounded @xl:size-9" />
          <div class="min-w-0 flex-1 space-y-1">
            <USkeleton class="h-4 w-28 max-w-full" />
            <USkeleton class="h-3 w-12" />
          </div>
        </div>
        <USkeleton class="mx-auto hidden size-5 rounded @2xl:block" />
        <USkeleton class="mx-auto size-7 rounded @4xl:hidden" />
        <div class="hidden items-center justify-center gap-2 @4xl:flex">
          <USkeleton class="size-[30px] rounded-md" />
          <USkeleton class="h-4 w-8" />
          <USkeleton class="size-[22px] rounded-full" />
          <USkeleton class="size-[22px] rounded" />
        </div>
        <USkeleton class="ml-auto h-4 w-7" />
        <USkeleton class="mx-auto size-6 rounded-full" />
        <USkeleton class="ml-auto hidden h-4 w-8 @xl:block" />
        <USkeleton class="ml-auto hidden h-4 w-7 @xl:block" />
        <USkeleton class="ml-auto hidden h-4 w-8 @xl:block" />
        <USkeleton class="ml-auto size-5 rounded-md" />
      </div>
    </div>

    <template v-else>
      <LeaderboardTruemainsTableRow
        v-for="row in rows"
        :key="row.rank"
        :row="row"
        :champions-by-id="championsById"
        :rune-tree="runeTree"
        :items-map="itemsMap"
        :patch="patch"
        :highlight-dedication="sort === 'dedication'"
      />
    </template>
  </div>
</template>
