<script setup lang="ts">
import { useElementSize } from '@vueuse/core'
import type { ChampionStaticListItem } from '~~/shared/types/static-data'
import { positionLabel, type ChampionPosition } from '#common/utils/positions'
import { isLoadingStatus } from '#common/utils/async-data'
import { championDisplayName } from '~~/shared/utils/champion-name'
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
  /** The page's lane; the panel starts there and follows it when it changes. */
  position: ChampionPosition | null
  champions: ChampionStaticListItem[]
  eloBracket?: string
  patch?: string | null
}>()

const ICON = 28
const ROW = 32
const MIN_ROWS = 5

const role = ref<ChampionPosition | null>(props.position)
watch(() => props.position, (value) => { role.value = value })

const { data, status, error } = useChampionMatchups(
  () => props.championId,
  () => role.value,
  { eloBracket: () => props.eloBracket, patch: () => props.patch },
)

// `idle` counts as loading: the fetch is client-only, and the server must not
// render a "no matchups" claim for a crawler to index (#1954).
const isLoading = computed(() => isLoadingStatus(status.value) && !data.value)

const championById = useChampionsById(() => props.champions)
const roleName = computed(() => (positionLabel(role.value) ?? 'lane').toLowerCase())

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

function describe(point: MatchupSpreadPoint): string {
  return `${opponentName(point)} — ${formatPercentage(point.laneWinRate, 0)} lanes won`
}

// Same deep link as the matchup rows: the `/matchup` tool with both champions
// and the role pinned (#939).
function matchupToolLink(opponentChampionId: number): string | undefined {
  if (!role.value) return undefined
  const query = new URLSearchParams({
    champion: String(props.championId),
    position: role.value,
    opponent: String(opponentChampionId),
  })
  return `/matchup?${query.toString()}`
}

const NuxtLinkComponent = resolveComponent('NuxtLink')
</script>

<template>
  <SectionCard :level="2" title="Matchups">
    <template #actions>
      <RolePicker hide-all :position="role" @update:position="value => (role = value)" />
    </template>

    <USkeleton v-if="isLoading" class="h-40 w-full rounded-md" />

    <FetchErrorAlert v-else-if="error" :error="error" title="Failed to load the matchups" />

    <UEmpty
      v-else-if="!hasPoints"
      size="sm"
      icon="i-lucide-swords"
      :description="`Not enough decided lanes against any ${roleName} opponent yet.`"
    />

    <div
      v-else
      ref="strip"
      class="relative overflow-hidden rounded-md bg-linear-to-r from-data-bad/15 via-transparent to-data-good/15"
      :style="{ height: `${layout.height}px` }"
    >
      <div class="absolute inset-y-2 left-1/2 w-px bg-accented" aria-hidden="true" />
      <UTooltip
        v-for="mark in layout.marks"
        :key="mark.point.entry.opponentChampionId"
        :text="describe(mark.point)"
      >
        <component
          :is="matchupToolLink(mark.point.entry.opponentChampionId) ? NuxtLinkComponent : 'div'"
          :to="matchupToolLink(mark.point.entry.opponentChampionId)"
          :aria-label="describe(mark.point)"
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
