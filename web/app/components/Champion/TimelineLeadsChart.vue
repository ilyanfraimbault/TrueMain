<script setup lang="ts">
import type { ChampionTimelineLeadsInterval } from '~~/shared/types/champions'

const props = withDefaults(defineProps<{
  intervals: ChampionTimelineLeadsInterval[]
  loading?: boolean
}>(), {
  loading: false,
})

// Each diff lives on its own scale (gold in hundreds, CS/kills in single
// digits), so a single chart shows one selected metric at a time rather than
// cramming incomparable series onto one axis.
type MetricKey = 'goldDiff' | 'csDiff' | 'killsDiff' | 'xpDiff' | 'damageDiff'

const METRICS: ReadonlyArray<{ key: MetricKey, label: string, digits: number }> = [
  { key: 'goldDiff', label: 'Gold', digits: 0 },
  { key: 'csDiff', label: 'CS', digits: 1 },
  { key: 'killsDiff', label: 'Kills', digits: 1 },
  { key: 'xpDiff', label: 'XP', digits: 0 },
  { key: 'damageDiff', label: 'Damage', digits: 0 },
]

const selectedKey = ref<MetricKey>('goldDiff')
const activeMetric = computed(() => METRICS.find(metric => metric.key === selectedKey.value) ?? METRICS[0]!)

interface ChartRow extends Record<string, unknown> {
  minute: number
  value: number
  games: number
}

const rows = computed<ChartRow[]>(() =>
  props.intervals.map(interval => ({
    minute: interval.intervalMinute,
    value: interval[selectedKey.value],
    games: interval.games,
  })),
)

const hasData = computed(() => rows.value.length > 0)

const PRIMARY = '#34d399' // emerald-400 (CHART_SERIES_PALETTE[0])
const categories = computed(() => ({ value: { name: activeMetric.value.label, color: PRIMARY } }))

const gradientStops = [
  { offset: '0%', stopOpacity: 0.4 },
  { offset: '100%', stopOpacity: 0.04 },
]

// Signed so the reader sees "ahead" vs "behind" at a glance; zero stays unsigned.
// Thousands (gold / xp / damage) abbreviate to keep the axis and tooltip compact.
function formatSigned(value: number, digits: number): string {
  const sign = value > 0 ? '+' : ''
  if (Math.abs(value) >= 1000) return `${sign}${(value / 1000).toFixed(1)}K`
  return `${sign}${value.toFixed(digits)}`
}

const formatGames = (count: number): string => count.toLocaleString('en-US')

const xFormatter = (tick: number): string => {
  const row = rows.value[tick]
  return row ? `${row.minute}m` : ''
}
const yFormatter = (value: number): string => formatSigned(value, activeMetric.value.digits)
</script>

<template>
  <SectionCard
    title="Lead vs lane opponent"
    subtitle="Average advantage over the opposing lane at each minute mark. Positive is ahead."
  >
    <template v-if="hasData" #actions>
      <div class="flex flex-wrap gap-1">
        <UButton
          v-for="metric in METRICS"
          :key="metric.key"
          size="xs"
          :color="metric.key === selectedKey ? 'primary' : 'neutral'"
          :variant="metric.key === selectedKey ? 'soft' : 'ghost'"
          @click="selectedKey = metric.key"
        >
          {{ metric.label }}
        </UButton>
      </div>
    </template>

    <USkeleton
      v-if="loading"
      class="h-[240px] w-full rounded-lg"
    />

    <p
      v-else-if="!hasData"
      class="py-8 text-center text-sm text-muted"
    >
      No timeline data yet for this champion and lane.
    </p>

    <ChartsAreaChart
      v-else
      :data="rows"
      :categories="categories"
      :height="240"
      :x-formatter="xFormatter"
      :y-formatter="yFormatter"
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
            {{ activeMetric.label }}: {{ formatSigned(values.value, activeMetric.digits) }}
          </p>
          <p class="mt-0.5 text-muted">
            {{ values.minute }} min · {{ formatGames(values.games) }} games
          </p>
        </div>
      </template>
    </ChartsAreaChart>
  </SectionCard>
</template>
