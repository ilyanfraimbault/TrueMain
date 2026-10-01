<!--
  One true main as a line of the app's truemains table: the site's
  `LeaderboardRow` (web/app/components/leaderboard/LeaderboardRow.vue) — rank,
  player, lanes, signature champion and its build, Truemain score, rank, games,
  KDA, win rate, follow — laid on the table's fixed grid (`TRUEMAINS_GRID`) so
  every column lines up down the list and under its header. The site's row
  sizes its columns with flex spacers, which at the app's width let the name
  push the rest off line (2026-09-30). A click opens the player's page on the
  site; the champion opens its builds here.
-->
<script setup lang="ts">
import type { LeaderboardRowResponse } from '#shared/types/leaderboard'
import type { ChampionStaticListItem, RuneTreeResponse, StaticItemData } from '#shared/types/static-data'
import { formatPercentage, getPositionIconUrl, getProfileIconUrl } from '#shared/utils/ddragon'
import { formatCount } from '#shared/utils/counts'
import { POSITION_BY_VALUE } from '#common/utils/positions'
import { isApexTier } from '~/utils/tiers'
import { winRateTone } from '#common/utils/rate-tone'
import { TRUEMAIN_SCORE_LABEL, dedicationParts, formatDedicationLastPlayed, formatDedicationScore } from '~/utils/dedication'
import { TRUEMAINS_GRID } from '~/utils/truemains-table'

const props = defineProps<{
  row: LeaderboardRowResponse
  championsById: Map<number, ChampionStaticListItem>
  runeTree: RuneTreeResponse | null
  itemsMap: Record<number, StaticItemData>
  patch: string | null
  highlightDedication?: boolean
}>()

const nameTag = computed(() => {
  const { gameName, tagLine } = props.row.identity
  return tagLine ? `${gameName}-${tagLine}` : gameName
})
const profileHref = computed(() => `/truemains/${encodeURIComponent(nameTag.value)}`)
const profileIconUrl = computed(() => getProfileIconUrl(props.row.identity.profileIconId, props.patch))
const isOtp = computed(() => props.row.topChampions.some(champion => champion.isOtp))
const ranked = computed(() => props.row.ranked)
const main = computed(() => props.row.topChampions[0] ?? null)

const { perk, perkStyle, item } = useBuildResolvers(() => props.runeTree, () => props.itemsMap)
const championName = (id: number) => props.championsById.get(id)?.name ?? `#${id}`
const championIcon = (id: number) => props.championsById.get(id)?.iconUrl ?? null

const lanes = computed(() => {
  const positions = props.row.positions
  if (!positions) return []
  return [positions.primary, positions.secondary]
    .filter((position): position is string => Boolean(position))
    .map((position, index) => ({ position, primary: index === 0, label: POSITION_BY_VALUE.get(position)?.label ?? position }))
})

const dedication = computed(() => props.row.dedication)
const stat = (value: number | null, format: (value: number) => string) => (value === null ? '—' : format(value))
</script>

<template>
  <div class="group relative grid h-14 items-center gap-2 border-b border-default/60 px-4 transition-colors hover:bg-accented" :class="TRUEMAINS_GRID">
    <button
      type="button"
      :aria-label="`${row.identity.gameName}${row.identity.tagLine ? ` #${row.identity.tagLine}` : ''} on truemain.lol`"
      class="absolute inset-0 z-[1] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-primary"
      @click="openOnSite(profileHref)"
    />

    <span class="text-sm font-semibold tabular-nums text-muted">#{{ row.rank }}</span>

    <div class="flex min-w-0 items-center gap-3">
      <SkeletonImage :src="profileIconUrl" :alt="row.identity.gameName" :width="36" :height="36" loading="lazy" class="size-9 shrink-0 rounded" />
      <div class="min-w-0">
        <div class="flex items-baseline gap-1">
          <span class="truncate text-sm font-bold text-default">{{ row.identity.gameName }}</span>
          <span v-if="row.identity.tagLine" class="shrink-0 text-[11px] text-muted">#{{ row.identity.tagLine }}</span>
          <span
            v-if="isOtp"
            class="shrink-0 self-center rounded-full bg-amber-400/25 px-1.5 py-0.5 text-[9px] font-bold uppercase leading-none tracking-wide text-amber-200 ring-1 ring-amber-400/50"
            title="One-trick pony"
          >OTP</span>
        </div>
        <LeaderboardRegionFlag :region="row.region" :width="16" class="mt-0.5" />
      </div>
    </div>

    <div class="flex items-center justify-center gap-1">
      <img
        v-for="lane in lanes"
        :key="lane.position"
        :src="getPositionIconUrl(lane.position)"
        :alt="lane.label"
        :title="`${lane.primary ? 'Primary' : 'Secondary'}: ${lane.label}`"
        class="size-5"
        :class="lane.primary ? '' : 'opacity-40'"
      >
    </div>

    <div class="relative z-10 flex justify-center">
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

    <UTooltip v-if="dedication" :delay-duration="150" :ui="{ content: 'p-0 h-auto max-w-none bg-transparent ring-0 shadow-none text-default' }">
      <span
        class="relative z-10 text-right text-sm font-semibold tabular-nums text-default"
        :class="{ 'underline decoration-dotted underline-offset-2': highlightDedication }"
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

    <UTooltip v-if="ranked" :delay-duration="150" :ui="{ content: 'p-0 h-auto max-w-none bg-transparent ring-0 shadow-none text-default' }">
      <div class="relative z-10 flex items-center justify-center gap-1">
        <RankIcon :tier="ranked.tier" :size="24" loading="lazy" />
        <span v-if="!isApexTier(ranked.tier)" class="text-xs font-semibold tabular-nums">{{ ranked.division }}</span>
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

    <span class="text-right text-sm tabular-nums text-default">{{ formatCount(row.stats.games) }}</span>
    <span class="text-right text-sm tabular-nums text-default">{{ stat(row.stats.kda, value => value.toFixed(1)) }}</span>
    <span class="text-right text-sm font-semibold tabular-nums" :class="winRateTone(row.stats.winRate)">{{ stat(row.stats.winRate, value => formatPercentage(value, 0)) }}</span>

    <div class="relative z-10 flex justify-end">
      <FavoriteToggle
        :game-name="row.identity.gameName"
        :tag-line="row.identity.tagLine"
        :region="row.region"
        :profile-icon-id="row.identity.profileIconId"
      />
    </div>
  </div>
</template>
