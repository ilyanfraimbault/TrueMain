<script setup lang="ts">
import type { ChampionTrendPoint } from '~~/shared/types/champions'
import { formatPercentage } from '~~/shared/utils/ddragon'

const props = withDefaults(defineProps<{
  points: ChampionTrendPoint[]
  loading?: boolean
}>(), {
  loading: false,
})

// Two area charts side by side: win rate and pick rate, each on its own clear
// scale. A combined dual-axis made the small pick-rate line look taller than
// the win-rate one, so they read better split. The per-patch win-rate
// movement (issue #112) is read straight off the left chart.
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

// Both series in the app primary (emerald-400). The charts sit side by side
// and never share a plot, so they don't need different hues to be told apart.
const PRIMARY = defaultSeriesColor(0) // app primary (rosegold-400)
const winRateCategories = { winRate: { name: 'Win rate', color: PRIMARY } }
const pickRateCategories = { pickRate: { name: 'Pick rate', color: PRIMARY } }

// Soft top-down fade for the filled area, matching ProfileRankedCard.
const gradientStops = [
  { offset: '0%', stopOpacity: 0.4 },
  { offset: '100%', stopOpacity: 0.04 },
]

const xFormatter = (tick: number): string => rows.value[tick]?.patch ?? ''
const winRateFormatter = (value: number): string => formatPercentage(value, 0)
const pickRateFormatter = (value: number): string => formatPercentage(value, 1)
</script>

<template>
  <SectionCard
    :level="2"
    title="Trend by patch"
    subtitle="Win rate and pick rate over the last five patches with data."
  >
    <USkeleton
      v-if="loading"
      class="h-[220px] w-full rounded-lg"
    />

    <p
      v-else-if="!hasTrend"
      class="py-8 text-center text-sm text-muted"
    >
      {{ hasData
        ? 'Only one patch of data so far — not enough history to chart a trend.'
        : 'No patch history yet for this champion and lane.' }}
    </p>

    <div v-else class="grid gap-4 sm:grid-cols-2">
      <div class="flex flex-col gap-1.5">
        <h3 class="text-xs font-medium text-muted">
          Win rate
        </h3>
        <ChartsAreaChart
          :data="rows"
          :categories="winRateCategories"
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
                {{ winRateFormatter(values.winRate) }}
              </p>
              <p class="mt-0.5 text-muted">
                Patch {{ values.patch }}
              </p>
            </div>
          </template>
        </ChartsAreaChart>
      </div>
      <div class="flex flex-col gap-1.5">
        <h3 class="text-xs font-medium text-muted">
          Pick rate
        </h3>
        <ChartsAreaChart
          :data="rows"
          :categories="pickRateCategories"
          :height="220"
          :x-formatter="xFormatter"
          :y-formatter="pickRateFormatter"
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
                {{ pickRateFormatter(values.pickRate) }}
              </p>
              <p class="mt-0.5 text-muted">
                Patch {{ values.patch }}
              </p>
            </div>
          </template>
        </ChartsAreaChart>
      </div>
    </div>
  </SectionCard>
</template>
