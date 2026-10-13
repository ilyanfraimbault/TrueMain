<script setup lang="ts">
import type { ChampionTrendPoint } from '~~/shared/types/champions'
import { formatPercentage } from '~~/shared/utils/ddragon'

const props = withDefaults(defineProps<{
  points: ChampionTrendPoint[]
  loading?: boolean
}>(), {
  loading: false,
})

// Area charts side by side — win rate, pick rate, and ban rate once it has
// history (#920) — each on its own clear scale. A combined dual-axis made the
// small pick-rate line look taller than the win-rate one, so they read better
// split. The per-patch win-rate movement (issue #112) is read straight off the
// left chart.
interface ChartRow extends Record<string, unknown> {
  patch: string
  winRate: number
  pickRate: number
  games: number
}

interface BanChartRow extends Record<string, unknown> {
  patch: string
  banRate: number
}

const rows = computed<ChartRow[]>(() =>
  props.points.map(point => ({
    patch: point.patch,
    winRate: point.winRate,
    pickRate: point.pickRate,
    games: point.games,
  })),
)

// Ban rate gets its own series rather than a third column on `rows`: it is null
// for every patch older than ban ingestion (#920), and the chart has no
// per-point null handling — a null would reach unovis as a gap in a line that is
// otherwise continuous. Filtering instead keeps the ban chart honest about the
// patches it actually covers, at the cost of its own x-axis indices.
const banRows = computed<BanChartRow[]>(() =>
  props.points
    .filter((point): point is ChampionTrendPoint & { banRate: number } => point.banRate !== null)
    .map(point => ({ patch: point.patch, banRate: point.banRate })),
)

const hasData = computed(() => rows.value.length > 0)

// A single observed patch can't show a trend — render the empty-state copy
// rather than a one-point line that reads as a flat bar.
const hasTrend = computed(() => rows.value.length > 1)

// Same rule for bans, applied to the patches that have ban data. Right after
// #920 ships that is zero or one patch, so the chart simply stays hidden until
// there is a real history to draw.
const hasBanTrend = computed(() => banRows.value.length > 1)

const winRateFormatter = (value: number): string => formatPercentage(value, 0)
const pickRateFormatter = (value: number): string => formatPercentage(value, 1)
const banRateFormatter = (value: number): string => formatPercentage(value, 1)

const chartGridClass = computed(() =>
  hasBanTrend.value ? 'grid gap-4 sm:grid-cols-2 lg:grid-cols-3' : 'grid gap-4 sm:grid-cols-2',
)
</script>

<template>
  <SectionCard
    :level="2"
    title="Trend by patch"
  >
    <USkeleton
      v-if="loading"
      class="h-[220px] w-full rounded-lg"
    />

    <UEmpty
      v-else-if="!hasTrend"
      size="sm"
      icon="i-lucide-trending-up"
      :description="hasData
        ? 'Only one patch of data so far — not enough history to chart a trend.'
        : 'No patch history yet for this champion and lane.'"
    />

    <div
      v-else
      :class="chartGridClass"
    >
      <ChampionTrendSeriesChart
        title="Win rate"
        :rows="rows"
        value-key="winRate"
        :format="winRateFormatter"
      />
      <ChampionTrendSeriesChart
        title="Pick rate"
        :rows="rows"
        value-key="pickRate"
        :format="pickRateFormatter"
      />
      <!-- Ban rate only appears once at least two patches have ban data (#920);
           its history starts at deploy and cannot be backfilled. -->
      <ChampionTrendSeriesChart
        v-if="hasBanTrend"
        title="Ban rate"
        :rows="banRows"
        value-key="banRate"
        :format="banRateFormatter"
      />
    </div>
  </SectionCard>
</template>
