<script setup lang="ts">
import type { LeaderboardRowResponse } from '~~/shared/types/leaderboard'
import type { ChampionStaticListItem, RuneTreeResponse, StaticItemData } from '~~/shared/types/static-data'
import { formatPercentage, getPositionIconUrl, getProfileIconUrl } from '~~/shared/utils/ddragon'
import { formatCount } from '~~/shared/utils/counts'
import { POSITION_BY_VALUE } from '~/utils/positions'
import { isApexTier } from '~/utils/tiers'
import { winRateTone } from '~/utils/rate-tone'
import { TRUEMAINS_TABLE_GRID } from '~/utils/list-tables'

// One line of the /truemains table (#1726): the figures `LeaderboardRow` shows,
// laid on the table's grid instead of flex spacers, so each sits under its
// column header and lines up down the list — the desktop app's table
// (desktop/app/app/components/leaderboard/TruemainsTableRow.vue). The compact
// `LeaderboardRow` stays where a list sits in a sidebar or a panel.
//
// The whole line opens the player's profile through a stretched overlay link;
// the champion cluster, the score and rank tooltips and the follow star lift
// themselves above it (`relative z-10`), so they are never an <a> in an <a>.
const props = defineProps<{
  row: LeaderboardRowResponse
  championsById: Map<number, ChampionStaticListItem>
  runeTree: RuneTreeResponse | null
  itemsMap: Record<number, StaticItemData>
  patch: string | null
  /** The board is ranked by Truemain score — underlines the figure that drives the order. */
  highlightDedication?: boolean
}>()

const nameTag = computed(() => {
  const { gameName, tagLine } = props.row.identity
  return tagLine ? `${gameName}-${tagLine}` : gameName
})
const profileHref = computed(() => `/truemains/${encodeURIComponent(nameTag.value)}`)
const profileAriaLabel = computed(() => {
  const { gameName, tagLine } = props.row.identity
  return tagLine ? `${gameName} #${tagLine}` : gameName
})
const profileIconUrl = computed(() => getProfileIconUrl(props.row.identity.profileIconId, props.patch))

// A player is an OTP of at most one champion, so any flagged top champion makes the whole player one.
const isOtp = computed(() => props.row.topChampions.some(champion => champion.isOtp))
const ranked = computed(() => props.row.ranked)
const main = computed(() => props.row.topChampions[0] ?? null)
const dedication = computed(() => props.row.dedication)

const { perk, perkStyle, item } = useBuildResolvers(() => props.runeTree, () => props.itemsMap)
const championName = (id: number) => props.championsById.get(id)?.name ?? `#${id}`
const championIcon = (id: number) => props.championsById.get(id)?.iconUrl ?? null
const canonicalIcon = useCanonicalIcon()

const lanes = computed(() => {
  const positions = props.row.positions
  if (!positions) return []
  return [positions.primary, positions.secondary]
    .filter((position): position is string => Boolean(position))
    .map((position, index) => {
      const label = POSITION_BY_VALUE.get(position)?.label ?? position
      return { position, primary: index === 0, title: `${index === 0 ? 'Primary' : 'Secondary'}: ${label}` }
    })
})

// Always two slots, padded with nulls: a variable count would move the cluster beside them.
const subChampions = computed(() => {
  const subs = props.row.topChampions.slice(1, 3)
  return [subs[0] ?? null, subs[1] ?? null]
})

// An em dash, never an empty cell, for a figure the aggregate cannot supply yet.
const stat = (value: number | null, format: (value: number) => string) => (value === null ? '—' : format(value))
</script>

<template>
  <div
    class="group relative grid h-12 items-center gap-2 border-b border-default/60 px-3 transition-colors last:border-b-0 hover:bg-accented"
    :class="TRUEMAINS_TABLE_GRID"
  >
    <NuxtLink
      :to="profileHref"
      :aria-label="profileAriaLabel"
      class="absolute inset-0 z-[1] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-primary"
    />

    <span class="text-xs font-semibold tabular-nums text-muted @xl:text-sm">{{ row.rank }}</span>

    <div class="flex min-w-0 items-center gap-2 @xl:gap-2.5">
      <SkeletonImage
        v-if="profileIconUrl"
        :src="profileIconUrl"
        :alt="row.identity.gameName"
        width="36"
        height="36"
        loading="lazy"
        class="size-7 shrink-0 rounded @xl:size-9"
      />
      <div v-else class="size-7 shrink-0 rounded bg-elevated/60 @xl:size-9" aria-hidden="true" />
      <div class="min-w-0">
        <div class="flex items-baseline gap-1">
          <span class="truncate text-sm font-bold text-default">{{ row.identity.gameName }}</span>
          <span v-if="row.identity.tagLine" class="hidden shrink-0 text-[11px] text-muted @xl:inline">#{{ row.identity.tagLine }}</span>
        </div>
        <!-- The OTP pill rides the flag's line, not the name's: on a narrow
             table the name is the column that truncates, and the pill took
             half of what it had. `relative z-10` keeps its tooltip above the
             stretched profile link. -->
        <div class="mt-0.5 flex items-center gap-1.5">
          <LeaderboardRegionFlag :region="row.region" :width="16" />
          <span
            v-if="isOtp"
            class="relative z-10 rounded-full bg-amber-400/25 px-1.5 py-px text-[9px] font-bold uppercase leading-none tracking-wide text-amber-200 ring-1 ring-amber-400/50"
            title="One-trick pony"
          >OTP</span>
        </div>
      </div>
    </div>

    <div class="hidden items-center justify-center gap-1 @2xl:flex">
      <img
        v-for="lane in lanes"
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

    <!-- The main: its portrait alone where the table is narrow, the full
         cluster from `@4xl`. Both link to the player's page for that champion. -->
    <div class="relative z-10 flex items-center justify-center gap-3">
      <ChampionLink
        v-if="main && championIcon(main.championId)"
        :champion-id="main.championId"
        :name="championName(main.championId)"
        :icon-url="championIcon(main.championId)"
        :name-tag="nameTag"
        :title="`${championName(main.championId)} · ${main.games} games`"
        class="size-7 @4xl:hidden"
      />
      <div class="hidden w-36 shrink-0 justify-center @4xl:flex">
        <LeaderboardChampionBuild
          v-if="main"
          :champion="main"
          :name="championName(main.championId)"
          :icon-url="championIcon(main.championId)"
          :name-tag="nameTag"
          :keystone="perk(main.primaryKeystoneId)"
          :secondary-style="perkStyle(main.secondaryStyleId)"
          :first-item="item(main.firstItemId)"
          reserve-slots
          loading="lazy"
        />
      </div>
      <div class="hidden w-[52px] shrink-0 items-center gap-1 @5xl:flex">
        <template v-for="(champ, index) in subChampions" :key="champ?.championId ?? `empty-${index}`">
          <ChampionLink
            v-if="champ && championIcon(champ.championId)"
            :champion-id="champ.championId"
            :name="championName(champ.championId)"
            :icon-url="championIcon(champ.championId)"
            :name-tag="nameTag"
            :title="`${championName(champ.championId)} · ${champ.games} games`"
            class="size-6"
          />
          <div v-else class="size-6 shrink-0" aria-hidden="true" />
        </template>
      </div>
    </div>

    <UTooltip
      v-if="dedication"
      :delay-duration="150"
      :ui="{ content: 'p-0 h-auto max-w-none bg-transparent ring-0 shadow-none text-default' }"
    >
      <span
        class="relative z-10 justify-self-end text-sm font-semibold tabular-nums text-default"
        :class="{ 'underline decoration-dotted underline-offset-2': highlightDedication }"
        data-testid="truemain-score"
      >{{ formatDedicationScore(dedication.score) }}</span>
      <template #content>
        <GameTooltipSurface>
          <div class="mb-2 flex w-64 items-center gap-2">
            <p class="min-w-0 flex-1 truncate text-xs font-semibold text-default">
              {{ TRUEMAIN_SCORE_LABEL }} {{ formatDedicationScore(dedication.score) }} · {{ championName(dedication.championId) }}
            </p>
            <DedicationVerdict :dedication="dedication" />
          </div>
          <DedicationBreakdown :parts="dedicationParts(dedication)" />
          <p v-if="formatDedicationLastPlayed(dedication.daysSinceLastPlayed)" class="mt-2 text-[10px] text-muted">
            {{ formatDedicationLastPlayed(dedication.daysSinceLastPlayed) }}
          </p>
        </GameTooltipSurface>
      </template>
    </UTooltip>
    <span v-else class="text-right text-sm text-dimmed">—</span>

    <UTooltip
      v-if="ranked"
      :delay-duration="150"
      :ui="{ content: 'p-0 h-auto max-w-none bg-transparent ring-0 shadow-none text-default' }"
    >
      <div class="relative z-10 flex items-center justify-center gap-1">
        <RankIcon :tier="ranked.tier" :size="24" loading="lazy" />
        <span v-if="!isApexTier(ranked.tier)" class="hidden text-xs font-semibold tabular-nums @xl:inline">{{ ranked.division }}</span>
      </div>
      <template #content>
        <GameTooltipSurface>
          <RankSummary
            :tier="ranked.tier"
            :division="ranked.division"
            :league-points="ranked.leaguePoints"
            :wins="row.stats.wins"
            :losses="row.stats.losses"
            :win-rate="row.stats.winRate"
            :size="32"
          />
        </GameTooltipSurface>
      </template>
    </UTooltip>
    <span v-else class="text-center text-sm text-dimmed">—</span>

    <span class="hidden text-right text-sm tabular-nums text-default @xl:block">{{ formatCount(row.stats.games) }}</span>
    <span class="hidden text-right text-sm tabular-nums text-default @xl:block">{{ stat(row.stats.kda, value => value.toFixed(1)) }}</span>
    <span
      class="hidden text-right text-sm font-semibold tabular-nums @xl:block"
      :class="winRateTone(row.stats.winRate)"
    >{{ stat(row.stats.winRate, value => formatPercentage(value, 0)) }}</span>

    <div class="flex justify-end">
      <FavoriteToggle
        :game-name="row.identity.gameName"
        :tag-line="row.identity.tagLine"
        :region="row.region"
        :profile-icon-id="row.identity.profileIconId"
      />
    </div>
  </div>
</template>
