<script setup lang="ts">
import type { ChampionTrendPoint } from '~~/shared/types/champions'
import { formatPercentage } from '~~/shared/utils/ddragon'

const props = withDefaults(defineProps<{
  points: ChampionTrendPoint[]
  loading?: boolean
}>(), {
  loading: false,
})

// Winrate and pickrate live on very different scales (≈45–55% vs a few
// percent), so a shared Y axis would flatten the pickrate line into the
// floor. We render two compact single-series charts instead — same emerald
// wrapper the rest of the app uses (see ChartsLineChart), stacked so the
// patch axis reads once down the left for both.
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

const winRateCategories = { winRate: { name: 'Win rate', color: '#34d399' } } // emerald-400
const pickRateCategories = { pickRate: { name: 'Pick rate', color: '#34d399' } }

const xFormatter = (tick: number): string => rows.value[tick]?.patch ?? ''
const yFormatter = (tick: number): string => formatPercentage(tick, 0)
</script>

<template>
  <section class="flex flex-col gap-4">
    <header class="flex flex-col gap-0.5">
      <h2 class="text-sm font-semibold">
        Trend by patch
      </h2>
      <p class="text-xs text-muted">
        Win rate and pick rate over the most recent patches with data.
      </p>
    </header>

    <USkeleton
      v-if="loading"
      class="h-[360px] w-full rounded-lg"
    />

    <p
      v-else-if="!hasTrend"
      class="rounded-lg bg-elevated/40 px-4 py-8 text-center text-sm text-muted"
    >
      {{ hasData
        ? 'Only one patch of data so far — not enough history to chart a trend.'
        : 'No patch history yet for this champion and lane.' }}
    </p>

    <div
      v-else
      class="grid gap-4 md:grid-cols-2"
    >
      <div class="flex flex-col gap-2 rounded-lg bg-elevated/40 p-4">
        <span class="text-xs font-medium uppercase tracking-wide text-muted">
          Win rate
        </span>
        <ChartsLineChart
          :data="rows"
          :categories="winRateCategories"
          :height="200"
          :x-formatter="xFormatter"
          :y-formatter="yFormatter"
          hide-legend
        />
      </div>

      <div class="flex flex-col gap-2 rounded-lg bg-elevated/40 p-4">
        <span class="text-xs font-medium uppercase tracking-wide text-muted">
          Pick rate
        </span>
        <ChartsLineChart
          :data="rows"
          :categories="pickRateCategories"
          :height="200"
          :x-formatter="xFormatter"
          :y-formatter="yFormatter"
          hide-legend
        />
      </div>
    </div>
  </section>
</template>
