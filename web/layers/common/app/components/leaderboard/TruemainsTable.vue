<script setup lang="ts">
import type { TableColumn } from '@nuxt/ui'
import type { LeaderboardRowResponse, LeaderboardSort } from '#shared/types/leaderboard'
import type { ChampionStaticListItem, RuneTreeResponse, StaticItemData } from '#shared/types/static-data'
import { riotIdLabel, truemainNameTag, truemainProfilePath } from '#shared/utils/truemain-path'
import { playerPath } from '#shared/utils/player-path'
import { isOneTrick, leaderboardStatLabels } from '#common/utils/leaderboard-row'
import { winRateTone } from '#common/utils/rate-tone'
import { leaderboardSortToSorting, sortingToLeaderboardSort, type TableSorting } from '#common/utils/table-sorting'
import { clickSelectableRow, wantsNewTab } from '#common/utils/table-rows'

// The /truemains leaderboard as a `UTable` (#1734). The page already holds one
// page of rows in the order the API ranked them; the table draws them and
// turns a header click into a new order. Five columns sort — Rank (LP), Score
// (Truemain score), Games, KDA and WR (#1737), the leaderboard's server orders,
// all descending — and a click only emits the new order: the page writes it to
// `?sort=`, which refetches page 1 from the API (manual sorting: the table
// never reorders rows itself).
//
// The table is an `@container`, and a column drops at a width by carrying the
// same `hidden @…:table-cell` on its header and its cells (column `meta`), so
// the two can no longer disagree. A whole row opens the player's profile
// (`@select`, plus Enter/Space through `clickSelectableRow`); the identity, the
// champion links and the follow star are their own controls inside it.
const props = defineProps<{
  rows: LeaderboardRowResponse[]
  sort: LeaderboardSort
  /** A refetch is in flight (filter, sort or page change) — the bar under the header. */
  loading: boolean
  championsById: Map<number, ChampionStaticListItem>
  runeTree: RuneTreeResponse | null
  itemsMap: Record<number, StaticItemData>
  patch: string | null
}>()

const emit = defineEmits<{ 'update:sort': [sort: LeaderboardSort] }>()

type Row = LeaderboardRowResponse

// Literal class strings, for Tailwind's scan.
const FROM_XL = 'hidden @xl:table-cell'
const FROM_2XL = 'hidden @2xl:table-cell'

const columns: TableColumn<Row>[] = [
  { id: 'place', header: '#', meta: { class: { th: 'w-6 @xl:w-10', td: 'w-6 @xl:w-10' } } },
  { id: 'player', header: 'Player', meta: { class: { th: 'w-full max-w-0', td: 'w-full max-w-0' } } },
  { id: 'lanes', header: 'Lanes', meta: { class: { th: `${FROM_2XL} text-center`, td: FROM_2XL } } },
  { id: 'main', header: 'Main', meta: { class: { th: 'text-center' } } },
  { id: 'dedication', accessorFn: row => row.dedication?.score ?? null, enableSorting: true, meta: { class: { th: 'text-right', td: 'text-right' } } },
  { id: 'rank', accessorFn: row => row.ranked?.score ?? null, enableSorting: true, meta: { class: { th: 'text-center' } } },
  { id: 'games', accessorFn: row => row.stats.games, enableSorting: true, meta: { class: { th: `${FROM_XL} text-right`, td: `${FROM_XL} text-right` } } },
  { id: 'kda', accessorFn: row => row.stats.kda, enableSorting: true, meta: { class: { th: `${FROM_XL} text-right`, td: `${FROM_XL} text-right` } } },
  { id: 'winRate', accessorFn: row => row.stats.winRate, enableSorting: true, meta: { class: { th: `${FROM_XL} text-right`, td: `${FROM_XL} text-right` } } },
  // No `header: ''`: a column header rendering an empty string leaves no node in
  // the server HTML, so its hydration swallows the slot's closing anchor and
  // the page reports a mismatch (#1590). The `#follow-header` slot names it.
  { id: 'follow', meta: { class: { th: 'w-7', td: 'w-7' } } },
]

const sorting = computed(() => leaderboardSortToSorting(props.sort))

function onSortingChange(next: TableSorting | undefined) {
  const sort = sortingToLeaderboardSort(next)
  if (sort !== props.sort) emit('update:sort', sort)
}

const nameTagOf = (row: Row) => truemainNameTag(row.identity.gameName, row.identity.tagLine)

function openProfile(event: Event, row: { original: Row }) {
  const path = truemainProfilePath(nameTagOf(row.original))
  void navigateTo(path, wantsNewTab(event) ? { open: { target: '_blank' } } : undefined)
}

const { championName, mainBuild } = useLeaderboardChampions(() => props.championsById, () => props.runeTree, () => props.itemsMap)

// Everything a row derives, once per row rather than once per cell.
const rowView = (row: Row) => ({
  nameTag: nameTagOf(row),
  main: mainBuild(row),
  otp: isOneTrick(row.topChampions),
  stats: leaderboardStatLabels(row.stats),
})
const views = computed(() => new Map(props.rows.map(row => [row, rowView(row)])))
const viewOf = (row: Row) => views.value.get(row) ?? rowView(row)
</script>

<template>
  <UTable
    :data="rows"
    :columns="columns"
    :sorting="sorting"
    :sorting-options="{ manualSorting: true, enableMultiSort: false, enableSortingRemoval: false, sortDescFirst: true }"
    :get-row-id="(row: Row) => `${row.identity.platformId}:${nameTagOf(row)}`"
    :watch-options="{ deep: false }"
    :loading="loading"
    class="@container"
    @update:sorting="onSortingChange"
    @select="openProfile"
    @keydown="clickSelectableRow"
  >
    <template #dedication-header="{ column }">
      <TableSortHeader :column="column" label="Score" title="Truemain score" align="end" @sort="column.toggleSorting(true)" />
    </template>
    <template #rank-header="{ column }">
      <TableSortHeader :column="column" label="Rank" title="rank (LP)" align="center" @sort="column.toggleSorting(true)" />
    </template>
    <template #games-header="{ column }">
      <TableSortHeader :column="column" label="Games" title="games on main champions" align="end" @sort="column.toggleSorting(true)" />
    </template>
    <template #kda-header="{ column }">
      <TableSortHeader :column="column" label="KDA" title="KDA on main champions" align="end" @sort="column.toggleSorting(true)" />
    </template>
    <template #winRate-header="{ column }">
      <TableSortHeader :column="column" label="WR" title="ranked win rate" align="end" @sort="column.toggleSorting(true)" />
    </template>
    <template #follow-header>
      <span class="sr-only">Follow</span>
    </template>

    <template #place-cell="{ row }">
      <span class="text-xs font-semibold tabular-nums text-muted @xl:text-sm">{{ row.original.rank }}</span>
    </template>

    <template #player-cell="{ row }">
      <Account
        :identity="row.original.identity"
        :region="row.original.region"
        :patch="patch"
        size="sm"
        loading="lazy"
        tabindex="-1"
        :ui="{ avatar: '@xl:size-9', tag: 'hidden @xl:inline' }"
      >
        <!-- A known player (#2016): their name links to their page, above the
             overlay that opens this account's profile; the Riot ID stays beside it. -->
        <template
          v-if="row.original.persona"
          #name
        >
          <div class="flex min-w-0 items-baseline gap-1.5">
            <NuxtLink
              :to="playerPath(row.original.persona.slug)"
              class="relative truncate text-sm font-bold text-highlighted"
              @click.stop
            >{{ row.original.persona.displayName }}</NuxtLink>
            <span class="hidden truncate text-[11px] text-muted @xl:inline">{{ riotIdLabel(row.original.identity.gameName, row.original.identity.tagLine) }}</span>
          </div>
        </template>
        <template v-if="viewOf(row.original).otp" #subline>
          <OtpBadge />
        </template>
      </Account>
    </template>

    <template #lanes-cell="{ row }">
      <LeaderboardLanes :positions="row.original.positions" class="flex justify-center" />
    </template>

    <template #main-cell="{ row }">
      <!-- The portrait alone where the table is narrow, the full cluster from @4xl. -->
      <LeaderboardChampions
        :main="viewOf(row.original).main"
        :top-champions="row.original.topChampions"
        :champions-by-id="championsById"
        :name-tag="viewOf(row.original).nameTag"
        portrait
        class="flex"
      />
    </template>

    <template #dedication-cell="{ row }">
      <LeaderboardScore
        v-if="row.original.dedication"
        :dedication="row.original.dedication"
        :champion-name="championName(row.original.dedication.championId)"
        :highlighted="sort === 'dedication'"
      />
      <span v-else class="text-dimmed">—</span>
    </template>

    <template #rank-cell="{ row }">
      <RankEmblem
        v-if="row.original.ranked"
        :rank="row.original.ranked"
        :record="row.original.stats"
        :ui="{ division: 'hidden @xl:inline' }"
        class="justify-center"
      />
      <span v-else class="block text-center text-dimmed">—</span>
    </template>

    <template #games-cell="{ row }">
      <span class="tabular-nums">{{ viewOf(row.original).stats.games }}</span>
    </template>
    <template #kda-cell="{ row }">
      <span class="tabular-nums">{{ viewOf(row.original).stats.kda }}</span>
    </template>
    <template #winRate-cell="{ row }">
      <span class="font-semibold tabular-nums" :class="winRateTone(row.original.stats.winRate)">
        {{ viewOf(row.original).stats.winRate }}
      </span>
    </template>

    <template #follow-cell="{ row }">
      <FavoriteToggle
        :game-name="row.original.identity.gameName"
        :tag-line="row.original.identity.tagLine"
        :region="row.original.region"
        :profile-icon-id="row.original.identity.profileIconId"
      />
    </template>
  </UTable>
</template>
