<script setup lang="ts">
import { useElementSize } from '@vueuse/core'
import type { ChampionStaticListItem } from '~~/shared/types/static-data'
import { ALL_POSITIONS, positionLabel, type PositionFilter } from '#common/utils/positions'
import { isLoadingStatus } from '#common/utils/async-data'
import { championDisplayName } from '~~/shared/utils/champion-name'
import { RESULT_SPREAD_WASH } from '#common/utils/result-tone'
import { formatPercentage } from '~~/shared/utils/ddragon'
import {
  beeswarmRows,
  buildMatchupSpread,
  matchupSpreadPosition,
  type MatchupSpreadPoint,
} from '~~/shared/utils/matchup-spread'

// Every lane opponent on one axis, hardest left to easiest right (#2094). The
// axis is the lane win rate at 15 minutes — ahead in gold over the opponent
// holding the same role — not the game win rate: the question this answers is
// "do I win the lane", which a game won by the jungler does not.
const props = defineProps<{
  championId: number
  /**
   * The page's lane, the only filter the panel takes: the opponent is whoever held
   * that role on the other team, and a lane picked here apart from the page's would
   * contradict the build beside it. `ALL` (player scope only) pairs each of the
   * player's games with the opponent of the position they held in it.
   */
  position: PositionFilter | null
  champions: ChampionStaticListItem[]
  /** When set, the player's own games and their own lanes (the player champion page). */
  nameTag?: string
  eloBracket?: string
  patch?: string | null
}>()

const ICON = 28
const ROW = 32
const MIN_ROWS = 5

const { data, status, error } = useChampionMatchups(
  () => props.championId,
  () => props.position,
  { nameTag: () => props.nameTag, eloBracket: () => props.eloBracket, patch: () => props.patch },
)

// `idle` counts as loading: the fetch is client-only, and the server must not
// render a "no matchups" claim for a crawler to index (#1954).
const isLoading = computed(() => isLoadingStatus(status.value) && !data.value)

const championById = useChampionsById(() => props.champions)
const isAllPositions = computed(() => props.position === ALL_POSITIONS)
const emptyDescription = computed(() => {
  if (isAllPositions.value) return 'Not enough games against any opponent yet.'
  const roleName = positionLabel(props.position)?.toLowerCase()
  return roleName
    ? `Not enough games against any ${roleName} opponent yet.`
    : 'Not enough games against any opponent yet.'
})

const spread = computed(() => buildMatchupSpread(data.value?.matchups ?? []))
const hasPoints = computed(() => spread.value.points.length > 0)

const strip = ref<HTMLElement | null>(null)
const { width } = useElementSize(strip)

// Icons are positioned over the strip minus one icon, so the outermost ones
// stay inside it; the beeswarm gap is one icon plus a hair in that same unit.
const layout = computed(() => {
  const points = spread.value.points
  const span = Math.max(1, width.value - ICON)
  const xs = points.map(p => matchupSpreadPosition(p.delta, spread.value.halfWidth))
  const rows = beeswarmRows(xs, (ICON + 4) / span)
  const low = Math.min(-1, ...rows)
  const high = Math.max(1, ...rows)
  const rowCount = Math.max(MIN_ROWS, high - low + 1)
  const offset = Math.floor((rowCount - (high - low + 1)) / 2)
  return {
    height: rowCount * ROW,
    marks: points.map((point, i) => ({
      point,
      left: xs[i]! * span,
      top: (rows[i]! - low + offset) * ROW + (ROW - ICON) / 2,
    })),
  }
})

function opponentName(point: MatchupSpreadPoint): string {
  const id = point.entry.opponentChampionId
  return championDisplayName(id, championById.value.get(id) ?? null)
}

// The tooltip sits on the opponent's own icon, so it states only the rate: a
// plain "WR", since "lanes won" reads wrong on a jungler (#2094's rate at 15 min
// is the same measure whatever the role). One player's games are a handful per
// opponent, so theirs also states the sample; the aggregate needs no such caveat.
function rateLabel(point: MatchupSpreadPoint): string {
  const rate = `${formatPercentage(point.laneWinRate, 0)} WR`
  if (!props.nameTag) return rate
  const games = point.entry.decidedLaneGames
  return `${rate} (${games} ${games === 1 ? 'game' : 'games'})`
}

// The icon is the only place the name shows, so the accessible name carries it.
function accessibleLabel(point: MatchupSpreadPoint): string {
  return `${opponentName(point)}: ${rateLabel(point)}`
}

// Same deep link as the matchup rows: the `/matchup` tool with both champions
// and the role pinned (#939).
function matchupToolLink(opponentChampionId: number): string | undefined {
  if (!props.position || isAllPositions.value) return undefined
  const query = new URLSearchParams({
    champion: String(props.championId),
    position: props.position,
    opponent: String(opponentChampionId),
  })
  return `/matchup?${query.toString()}`
}

const NuxtLinkComponent = resolveComponent('NuxtLink')
</script>

<template>
  <SectionCard :level="2" title="Matchups">

    <USkeleton v-if="isLoading" class="h-40 w-full rounded-md" />

    <FetchErrorAlert v-else-if="error" :error="error" title="Failed to load the matchups" />

    <UEmpty
      v-else-if="!hasPoints"
      size="sm"
      icon="i-lucide-swords"
      :description="emptyDescription"
    />

    <div
      v-else
      ref="strip"
      class="relative overflow-hidden rounded-md"
      :class="RESULT_SPREAD_WASH"
      :style="{ height: `${layout.height}px` }"
    >
      <div class="absolute inset-y-2 left-1/2 w-px bg-accented" aria-hidden="true" />
      <UTooltip
        v-for="mark in layout.marks"
        :key="mark.point.entry.opponentChampionId"
        :text="rateLabel(mark.point)"
      >
        <component
          :is="matchupToolLink(mark.point.entry.opponentChampionId) ? NuxtLinkComponent : 'div'"
          :to="matchupToolLink(mark.point.entry.opponentChampionId)"
          :aria-label="accessibleLabel(mark.point)"
          class="absolute rounded focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary"
          :style="{ left: `${mark.left}px`, top: `${mark.top}px`, width: `${ICON}px`, height: `${ICON}px` }"
        >
          <SkeletonImage
            v-if="championById.get(mark.point.entry.opponentChampionId)?.iconUrl"
            :src="championById.get(mark.point.entry.opponentChampionId)!.iconUrl"
            :alt="opponentName(mark.point)"
            :width="ICON"
            :height="ICON"
            loading="lazy"
            class="size-full rounded"
          />
          <div v-else class="size-full rounded bg-elevated" />
        </component>
      </UTooltip>
    </div>
  </SectionCard>
</template>
