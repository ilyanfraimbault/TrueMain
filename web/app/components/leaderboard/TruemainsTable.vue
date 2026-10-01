<script setup lang="ts">
import type { TableColumn } from '@nuxt/ui'
import type { LeaderboardRowResponse, LeaderboardSort } from '~~/shared/types/leaderboard'
import type { ChampionStaticListItem, RuneTreeResponse, StaticItemData } from '~~/shared/types/static-data'
import { formatPercentage, getPositionIconUrl } from '~~/shared/utils/ddragon'
import { formatCount } from '~~/shared/utils/counts'
import { truemainNameTag, truemainProfilePath } from '~~/shared/utils/truemain-path'
import { POSITION_BY_VALUE } from '#common/utils/positions'
import { isApexTier } from '~/utils/tiers'
import { winRateTone } from '#common/utils/rate-tone'
import { leaderboardSortToSorting, sortingToLeaderboardSort, type TableSorting } from '~/utils/table-sorting'
import { clickSelectableRow, wantsNewTab } from '~/utils/table-rows'

// The /truemains leaderboard as a `UTable` (#1734). The page already holds one
// page of rows in the order the API ranked them; the table draws them and
// turns a header click into a new order. Two columns sort — Rank (LP) and
// Score (Truemain score), the leaderboard's two server orders, both
// descending — and a click only emits the new order: the page writes it to
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
  { id: 'games', header: 'Games', meta: { class: { th: `${FROM_XL} text-right`, td: `${FROM_XL} text-right` } } },
  { id: 'kda', header: 'KDA', meta: { class: { th: `${FROM_XL} text-right`, td: `${FROM_XL} text-right` } } },
  { id: 'winRate', header: 'WR', meta: { class: { th: `${FROM_XL} text-right`, td: `${FROM_XL} text-right` } } },
  { id: 'follow', header: '', meta: { class: { th: 'w-7', td: 'w-7' } } },
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

const { perk, perkStyle, item } = useBuildResolvers(() => props.runeTree, () => props.itemsMap)
const championName = (id: number) => props.championsById.get(id)?.name ?? `#${id}`
const championIcon = (id: number) => props.championsById.get(id)?.iconUrl ?? null
const canonicalIcon = useCanonicalIcon()

function lanesOf(row: Row) {
  const positions = row.positions
  if (!positions) return []
  return [positions.primary, positions.secondary]
    .filter((position): position is string => Boolean(position))
    .map((position, index) => ({
      position,
      primary: index === 0,
      title: `${index === 0 ? 'Primary' : 'Secondary'}: ${POSITION_BY_VALUE.get(position)?.label ?? position}`,
    }))
}

// Always two slots: a variable count would shift the cluster beside them.
const subChampionsOf = (row: Row) => [row.topChampions[1] ?? null, row.topChampions[2] ?? null]

// A player is an OTP of at most one champion, so any flagged top champion makes the whole player one.
const isOtp = (row: Row) => row.topChampions.some(champion => champion.isOtp)

// An em dash, never an empty cell, for a figure the aggregate cannot supply yet.
const stat = (value: number | null, format: (value: number) => string) => (value === null ? '—' : format(value))

const TOOLTIP_UI = { content: 'p-0 h-auto max-w-none bg-transparent ring-0 shadow-none text-default' }
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
        <template v-if="isOtp(row.original)" #subline>
          <span
            class="rounded-full bg-amber-400/25 px-1.5 py-px text-[9px] font-bold uppercase leading-none tracking-wide text-amber-200 ring-1 ring-amber-400/50"
            title="One-trick pony"
          >OTP</span>
        </template>
      </Account>
    </template>

    <template #lanes-cell="{ row }">
      <div class="flex items-center justify-center gap-1">
        <img
          v-for="lane in lanesOf(row.original)"
          :key="lane.position"
          :src="canonicalIcon(getPositionIconUrl(lane.position))"
          :alt="lane.title"
          :title="lane.title"
          class="size-5 shrink-0"
          :class="lane.primary ? undefined : 'opacity-40'"
          width="20"
          height="20"
          loading="lazy"
        >
      </div>
    </template>

    <template #main-cell="{ row }">
      <div v-if="row.original.topChampions[0]" class="flex items-center justify-center gap-3">
        <!-- The portrait alone where the table is narrow, the full cluster from @4xl. -->
        <ChampionLink
          v-if="championIcon(row.original.topChampions[0].championId)"
          :champion-id="row.original.topChampions[0].championId"
          :name="championName(row.original.topChampions[0].championId)"
          :icon-url="championIcon(row.original.topChampions[0].championId)"
          :name-tag="nameTagOf(row.original)"
          :title="`${championName(row.original.topChampions[0].championId)} · ${row.original.topChampions[0].games} games`"
          class="size-7 @4xl:hidden"
        />
        <div class="hidden w-36 shrink-0 justify-center @4xl:flex">
          <LeaderboardChampionBuild
            :champion="row.original.topChampions[0]"
            :name="championName(row.original.topChampions[0].championId)"
            :icon-url="championIcon(row.original.topChampions[0].championId)"
            :name-tag="nameTagOf(row.original)"
            :keystone="perk(row.original.topChampions[0].primaryKeystoneId)"
            :secondary-style="perkStyle(row.original.topChampions[0].secondaryStyleId)"
            :first-item="item(row.original.topChampions[0].firstItemId)"
            reserve-slots
            loading="lazy"
          />
        </div>
        <div class="hidden w-[52px] shrink-0 items-center gap-1 @5xl:flex">
          <template v-for="(champ, index) in subChampionsOf(row.original)" :key="champ?.championId ?? `empty-${index}`">
            <ChampionLink
              v-if="champ && championIcon(champ.championId)"
              :champion-id="champ.championId"
              :name="championName(champ.championId)"
              :icon-url="championIcon(champ.championId)"
              :name-tag="nameTagOf(row.original)"
              :title="`${championName(champ.championId)} · ${champ.games} games`"
              class="size-6"
            />
            <div v-else class="size-6 shrink-0" aria-hidden="true" />
          </template>
        </div>
      </div>
    </template>

    <template #dedication-cell="{ row }">
      <UTooltip v-if="row.original.dedication" :delay-duration="150" :ui="TOOLTIP_UI">
        <span
          class="font-semibold tabular-nums"
          :class="{ 'underline decoration-dotted underline-offset-2': sort === 'dedication' }"
          data-testid="truemain-score"
        >{{ formatDedicationScore(row.original.dedication.score) }}</span>
        <template #content>
          <GameTooltipSurface>
            <div class="mb-2 flex w-64 items-center gap-2">
              <p class="min-w-0 flex-1 truncate text-xs font-semibold text-default">
                {{ TRUEMAIN_SCORE_LABEL }} {{ formatDedicationScore(row.original.dedication.score) }} · {{ championName(row.original.dedication.championId) }}
              </p>
              <DedicationVerdict :dedication="row.original.dedication" />
            </div>
            <DedicationBreakdown :parts="dedicationParts(row.original.dedication)" />
            <p v-if="formatDedicationLastPlayed(row.original.dedication.daysSinceLastPlayed)" class="mt-2 text-[10px] text-muted">
              {{ formatDedicationLastPlayed(row.original.dedication.daysSinceLastPlayed) }}
            </p>
          </GameTooltipSurface>
        </template>
      </UTooltip>
      <span v-else class="text-dimmed">—</span>
    </template>

    <template #rank-cell="{ row }">
      <UTooltip v-if="row.original.ranked" :delay-duration="150" :ui="TOOLTIP_UI">
        <div class="flex items-center justify-center gap-1">
          <RankIcon :tier="row.original.ranked.tier" :size="24" loading="lazy" />
          <span v-if="!isApexTier(row.original.ranked.tier)" class="hidden text-xs font-semibold tabular-nums @xl:inline">{{ row.original.ranked.division }}</span>
        </div>
        <template #content>
          <GameTooltipSurface>
            <RankSummary
              :tier="row.original.ranked.tier"
              :division="row.original.ranked.division"
              :league-points="row.original.ranked.leaguePoints"
              :wins="row.original.stats.wins"
              :losses="row.original.stats.losses"
              :win-rate="row.original.stats.winRate"
              :size="32"
            />
          </GameTooltipSurface>
        </template>
      </UTooltip>
      <span v-else class="block text-center text-dimmed">—</span>
    </template>

    <template #games-cell="{ row }">
      <span class="tabular-nums">{{ formatCount(row.original.stats.games) }}</span>
    </template>
    <template #kda-cell="{ row }">
      <span class="tabular-nums">{{ stat(row.original.stats.kda, value => value.toFixed(1)) }}</span>
    </template>
    <template #winRate-cell="{ row }">
      <span class="font-semibold tabular-nums" :class="winRateTone(row.original.stats.winRate)">
        {{ stat(row.original.stats.winRate, value => formatPercentage(value, 0)) }}
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
