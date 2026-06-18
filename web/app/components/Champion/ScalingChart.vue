<script setup lang="ts">
import type { ChampionScalingBucket } from '~~/shared/types/champions'
import { formatPercentage } from '~~/shared/utils/ddragon'

const props = withDefaults(defineProps<{
  buckets: ChampionScalingBucket[]
  scalingIndex: number | null
  loading?: boolean
}>(), {
  loading: false,
})

interface ChartRow extends Record<string, unknown> {
  label: string
  winRate: number
  games: number
}

const rows = computed<ChartRow[]>(() =>
  props.buckets.map(bucket => ({
    label: bucket.label,
    winRate: bucket.winRate,
    games: bucket.games,
  })),
)

const hasData = computed(() => rows.value.length > 0)

// A scaling read needs both a short and a long bucket to chart a trend; a single
// bucket is a lone point, so fall back to the empty-state copy like TrendChart.
const hasTrend = computed(() => rows.value.length > 1)

// Qualitative read of the index (a win-rate gap, e.g. 0.04 = +4 points). Three
// points either way is the "meaningfully scales / fades" line; inside it is flat.
const SCALING_THRESHOLD = 0.03
const verdict = computed<{ label: string, tone: string } | null>(() => {
  if (props.scalingIndex === null) return null
  if (props.scalingIndex >= SCALING_THRESHOLD) return { label: 'Scales late', tone: 'text-primary' }
  if (props.scalingIndex <= -SCALING_THRESHOLD) return { label: 'Early game', tone: 'text-warning' }
  return { label: 'Even', tone: 'text-muted' }
})

const formatSignedPercent = (value: number): string =>
  `${value > 0 ? '+' : value < 0 ? '−' : ''}${formatPercentage(Math.abs(value), 1)}`

const PRIMARY = '#34d399' // emerald-400 (CHART_SERIES_PALETTE[0])
const categories = { winRate: { name: 'Win rate', color: PRIMARY } }

const gradientStops = [
  { offset: '0%', stopOpacity: 0.4 },
  { offset: '100%', stopOpacity: 0.04 },
]

const xFormatter = (tick: number): string => rows.value[tick]?.label ?? ''
const winRateFormatter = (value: number): string => formatPercentage(value, 0)
</script>

<template>
  <section class="flex flex-col gap-4">
    <header class="flex flex-wrap items-center justify-between gap-2">
      <div class="flex flex-col gap-0.5">
        <h2 class="text-sm font-semibold">
          Scaling
        </h2>
        <p class="text-xs text-muted">
          Win rate by game length. A rising line means the champion scales into the late game.
        </p>
      </div>

      <div
        v-if="verdict"
        class="flex items-baseline gap-1.5 text-xs"
      >
        <span class="font-semibold tabular-nums" :class="verdict.tone">
          {{ formatSignedPercent(scalingIndex ?? 0) }}
        </span>
        <span :class="verdict.tone">{{ verdict.label }}</span>
      </div>
    </header>

    <USkeleton
      v-if="loading"
      class="h-[220px] w-full rounded-lg"
    />

    <p
      v-else-if="!hasTrend"
      class="glass rounded-lg px-4 py-8 text-center text-sm text-muted"
    >
      {{ hasData
        ? 'Only one duration bucket has enough games — not enough to chart scaling.'
        : 'No game-length data yet for this champion and lane.' }}
    </p>

    <ChartsAreaChart
      v-else
      :data="rows"
      :categories="categories"
      :height="220"
      :x-formatter="xFormatter"
      :y-formatter="winRateFormatter"
      :gradient-stops="gradientStops"
      :y-grid-line="false"
      hide-legend
    >
      <template #tooltip="{ values }">
        <div
          v-if="values"
          class="rounded-md border border-default bg-elevated px-2 py-1.5 text-xs shadow-md"
        >
          <p class="font-semibold tabular-nums text-default">
            {{ winRateFormatter(values.winRate) }} win rate
          </p>
          <p class="mt-0.5 text-muted">
            {{ values.label }} · {{ values.games.toLocaleString('en-US') }} games
          </p>
        </div>
      </template>
    </ChartsAreaChart>
  </section>
</template>
