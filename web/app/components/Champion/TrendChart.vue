<script setup lang="ts">
import type { ChampionTrendPoint } from '~~/shared/types/champions'
import { formatPercentage } from '~~/shared/utils/ddragon'

const props = withDefaults(defineProps<{
  points: ChampionTrendPoint[]
  loading?: boolean
}>(), {
  loading: false,
})

// One chart, two series, two Y axes. Win rate (≈50%) reads off the left
// axis at its real scale; pick rate (a few %) reads off the right axis on
// its own scale, so a buff that lifts both is visible at a glance and the
// per-patch win-rate movement (issue #112) is read straight off the line.
interface ChartRow extends Record<string, unknown> {
  patch: string
  winRate: number
  pickRate: number
  games: number
}

const rows = computed<ChartRow[]>(() =>
  props.points.map(point => ({
    patch: point.patch,
    winRate: point.winRate,
    pickRate: point.pickRate,
    games: point.games,
  })),
)

const hasData = computed(() => rows.value.length > 0)

// A single observed patch can't show a trend — render the empty-state copy
// rather than a one-point line that reads as a flat bar.
const hasTrend = computed(() => rows.value.length > 1)

// Two emerald-family shades so the lines stay on-palette but distinct:
// the bright primary for win rate, a deeper emerald for pick rate.
const categories = {
  winRate: { name: 'Win rate', color: '#34d399' }, // emerald-400
  pickRate: { name: 'Pick rate', color: '#0f766e' }, // emerald-700
}

const xFormatter = (tick: number): string => rows.value[tick]?.patch ?? ''
const winRateFormatter = (value: number): string => formatPercentage(value, 0)
const pickRateFormatter = (value: number): string => formatPercentage(value, 1)
</script>

<template>
  <section class="flex flex-col gap-4">
    <header class="flex flex-col gap-0.5">
      <h2 class="text-sm font-semibold">
        Trend by patch
      </h2>
      <p class="text-xs text-muted">
        Win rate and pick rate over the last five patches with data.
      </p>
    </header>

    <USkeleton
      v-if="loading"
      class="h-[300px] w-full rounded-lg"
    />

    <p
      v-else-if="!hasTrend"
      class="rounded-lg bg-elevated/40 px-4 py-8 text-center text-sm text-muted"
    >
      {{ hasData
        ? 'Only one patch of data so far — not enough history to chart a trend.'
        : 'No patch history yet for this champion and lane.' }}
    </p>

    <ChartsDualAxisLineChart
      v-else
      :data="rows"
      :categories="categories"
      primary-key="winRate"
      secondary-key="pickRate"
      :height="300"
      :x-formatter="xFormatter"
      :primary-formatter="winRateFormatter"
      :secondary-formatter="pickRateFormatter"
    />
  </section>
</template>
