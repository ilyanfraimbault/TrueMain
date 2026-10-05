<script setup lang="ts">
// Riot API tab: call volume per bucket as GROUPED BARS (#1218). Calls-per-bucket is
// a flow, and the window fixes the bucket size server-side (5m / 1h / 6h) to land
// 12-28 buckets, so bars stay wide enough to read at every window.
// Retries (429s) are a second series (#1035): budget spent for no data, worth
// seeing distinctly from the total volume rather than buried in "errors". Grouped
// and never stacked — `Retries` is documented as a SUBSET of `Calls`, so a stack
// would draw a total that counts every 429 twice.
import type { RiotUsageBucket, RiotUsageWindow } from '~~/shared/types/ops'

const props = defineProps<{
  timeSeries: RiotUsageBucket[]
  usageWindow: RiotUsageWindow
  pending: boolean
}>()

const timeSeriesData = computed(() =>
  props.timeSeries.map(bucket => ({
    label: formatCallBucketLabel(bucket.bucketUtc),
    calls: bucket.calls,
    retries: bucket.retries,
  })),
)
const timeSeriesCategories = {
  calls: { name: 'Calls', color: CHART_SERIES[0] },
  retries: { name: 'Retries (429)', color: CHART_SERIES[1] },
}
const timeSeriesXFormatter = computed(() =>
  indexLabelFormatter(timeSeriesData.value, row => row.label),
)
// Named distinctly from the auto-imported `formatBucketLabel` (charts.ts),
// which formats matches-over-time buckets per granularity.
function formatCallBucketLabel(iso: string): string {
  const date = new Date(iso)
  if (Number.isNaN(date.getTime())) {
    return iso
  }
  if (props.usageWindow === '30d') {
    return date.toLocaleDateString('en-US', { month: 'short', day: 'numeric' })
  }
  if (props.usageWindow === '7d') {
    return date.toLocaleString('en-US', {
      month: 'short',
      day: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
      hour12: false,
    })
  }
  return date.toLocaleTimeString('en-US', {
    hour: '2-digit',
    minute: '2-digit',
    hour12: false,
  })
}
</script>

<template>
  <UCard class="mb-6" :ui="{ root: 'overflow-visible' }">
    <template #header>
      <PanelTitle variant="label" title="Call volume over time" />
    </template>
    <USkeleton v-if="pending" class="h-[260px] w-full" />
    <div
      v-else-if="timeSeriesData.length === 0"
      class="flex h-[260px] items-center justify-center text-sm text-muted"
    >
      No calls recorded in this window.
    </div>
    <ChartsBarChart
      v-else
      :data="timeSeriesData"
      :height="260"
      :categories="timeSeriesCategories"
      :y-axis="['calls', 'retries']"
      :x-num-ticks="Math.min(timeSeriesData.length, 8)"
      :x-formatter="timeSeriesXFormatter"
      :y-formatter="formatCount"
      :tooltip-title-formatter="labelTooltipTitle"
      v-bind="multiTimeBarProps()"
    />
  </UCard>
</template>
