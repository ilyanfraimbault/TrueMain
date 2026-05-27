<script setup lang="ts">
import type { RankHistoryEntry } from '~~/shared/types/rank-history'
import { isApexTier, rankScore, tierFloor, tierHex, TIER_NAMES } from '~/utils/tiers'

const props = defineProps<{
  entries: readonly RankHistoryEntry[]
  /** Current (most recent) tier — drives the fill colour. */
  currentTier: string | null
  loading?: boolean
}>()

const CHART_HEIGHT = 160

interface ChartPoint extends Record<string, unknown> {
  i: number
  score: number
  entry: RankHistoryEntry
}

const chartPoints = computed<ChartPoint[]>(() =>
  props.entries.map((entry, i) => ({
    i,
    score: rankScore(entry.tier, entry.division, entry.leaguePoints),
    entry,
  })),
)

const fillColor = computed(() => tierHex(props.currentTier))

const categories = computed(() => ({
  score: { name: 'Rank', color: fillColor.value },
}))

// Y range — pad by half a tier on each side, then snap to tier floors so
// the rendered Y bounds align with the icons we overlay. Falls back to
// [0, 400] (Iron band) when there's nothing to plot so the empty chart
// has a sensible scale.
const yDomain = computed<[number, number]>(() => {
  if (chartPoints.value.length === 0) return [0, 400]
  const scores = chartPoints.value.map(p => p.score)
  const minScore = Math.min(...scores)
  const maxScore = Math.max(...scores)
  const padded = Math.max(50, (maxScore - minScore) * 0.25)
  return [Math.max(0, minScore - padded), maxScore + padded]
})

// Tier crests rendered alongside the chart. For sub-apex players we show
// every tier whose floor falls inside the visible Y range, so promotions
// and demotions read at a glance. Apex tiers (Master / GM / Challenger)
// collapse to a single continuous LP band above the Master floor, so
// there's no meaningful "tier band" to mark — instead we pin a single
// crest for the current tier at the top of the column.
const visibleTiers = computed(() => {
  if (props.currentTier && isApexTier(props.currentTier)) {
    return [{ tier: props.currentTier.toUpperCase(), floor: yDomain.value[1] }]
  }
  const [yMin, yMax] = yDomain.value
  return TIER_NAMES
    .map(tier => ({ tier, floor: tierFloor(tier) }))
    .filter(({ floor }) => floor >= yMin - 200 && floor <= yMax + 200)
})

// Vertical offset (in px from the top of the chart container) for each
// visible tier icon. Inverted because SVG/Y grows downwards while rank
// grows upwards.
function tierTopPx(floor: number): number {
  const [yMin, yMax] = yDomain.value
  if (yMax === yMin) return CHART_HEIGHT / 2
  const ratio = (floor - yMin) / (yMax - yMin)
  return CHART_HEIGHT * (1 - ratio)
}

function dateLabel(iso: string): string {
  return new Date(iso).toLocaleDateString(undefined, {
    month: 'short',
    day: 'numeric',
  })
}

const xFormatter = (tick: number): string => {
  const point = chartPoints.value[tick]
  return point ? dateLabel(point.entry.capturedAtUtc) : ''
}

const isUnranked = computed(() =>
  !props.loading && chartPoints.value.length === 0,
)
</script>

<template>
  <section class="rounded-lg bg-elevated/40 px-4 py-3">
    <h2 class="text-xs font-semibold uppercase tracking-wide text-muted">
      Rank history
    </h2>

    <USkeleton v-if="loading" class="mt-3 h-40 w-full rounded-md" />

    <p v-else-if="isUnranked" class="mt-2 text-sm text-muted">
      No ranked snapshots in the last 90 days.
    </p>

    <div v-else class="mt-3 flex gap-2">
      <!-- Y-axis: tier crests stacked at their score band. The wrapping
           column shares the chart's exact height so absolute offsets in
           `tierTopPx` line up with the data range. -->
      <div
        class="relative w-7 shrink-0"
        :style="{ height: `${CHART_HEIGHT}px` }"
        aria-hidden="true"
      >
        <RankIcon
          v-for="band in visibleTiers"
          :key="band.tier"
          :tier="band.tier"
          :size="20"
          class="absolute left-0 -translate-y-1/2"
          :style="{ top: `${tierTopPx(band.floor)}px` }"
        />
      </div>

      <div class="min-w-0 flex-1">
        <ChartsAreaChart
          :data="chartPoints"
          :categories="categories"
          :height="CHART_HEIGHT"
          :x-formatter="xFormatter"
          :gradient-stops="[
            { offset: '0%', stopOpacity: 0.45 },
            { offset: '100%', stopOpacity: 0.05 },
          ]"
          hide-y-axis
          hide-legend
        >
          <template #tooltip="{ values }">
            <div
              v-if="values"
              class="rounded-md border border-default bg-elevated px-2 py-1.5 text-xs shadow-md"
            >
              <div class="flex items-center gap-1.5">
                <RankIcon :tier="values.entry.tier" :size="20" />
                <span class="font-semibold tabular-nums text-default">
                  {{ values.entry.leaguePoints }} LP
                </span>
              </div>
              <p class="mt-0.5 text-muted">
                {{ dateLabel(values.entry.capturedAtUtc) }}
              </p>
            </div>
          </template>
        </ChartsAreaChart>
      </div>
    </div>
  </section>
</template>
