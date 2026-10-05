<script setup lang="ts">
// Overview card: main candidates by status, from the `candidatesByStatus` block of
// `GET /api/ops/stats/overview` (fetched by the page).
import type { OverviewStats } from '~~/shared/types/ops'
import { formatNumber } from '~~/shared/utils/format'

const props = defineProps<{
  stats: OverviewStats | null | undefined
  pending: boolean
}>()

// Candidate pipeline buckets as ordered (label, count) pairs. The colors trace
// the New -> Validated/Rejected flow using the semantic status vocabulary.
const candidateBuckets = computed(() => {
  const c = props.stats?.candidatesByStatus
  if (!c) {
    return []
  }
  return [
    { label: 'New', count: c.New, color: 'neutral' as const },
    { label: 'Scored', count: c.Scored, color: 'info' as const },
    { label: 'Queued', count: c.Queued, color: 'warning' as const },
    { label: 'Processing', count: c.Processing, color: 'warning' as const },
    { label: 'Validated', count: c.Validated, color: 'success' as const },
    { label: 'Rejected', count: c.Rejected, color: 'error' as const },
  ]
})

const candidatesTotal = computed(() =>
  candidateBuckets.value.reduce((sum, b) => sum + (b.count ?? 0), 0),
)

// Horizontal-bar series for the candidate pipeline. Single rosegold series.
// Rendered horizontally, so the bucket label lives on the LEFT (category) axis:
// `candidateLabelFormatter` looks the label up by bar index for that y-axis.
const candidateChartData = computed(() =>
  candidateBuckets.value.map(b => ({ label: b.label, count: b.count ?? 0 })),
)
const candidateChartCategories = {
  count: { name: 'Candidates', color: CHART_PRIMARY },
}
// Chart grows with the number of bars; the skeleton mirrors it to avoid CLS.
const candidateChartHeight = computed(() =>
  barChartHeight(candidateChartData.value.length, { min: 200, step: 34 }),
)
// Wrapped in a computed so the label lookup tracks `candidateChartData`
// instead of closing over its initial (empty) value before stats load.
const candidateLabelFormatter = computed(() =>
  indexLabelFormatter(candidateChartData.value, row => row.label),
)
</script>

<template>
  <UCard :ui="{ root: 'overflow-visible' }">
    <template #header>
      <div class="flex items-center justify-between">
        <PanelTitle
          variant="label"
          title="Candidate pipeline"
          subtitle="Main candidates by status."
        />
        <UBadge
          v-if="!pending"
          color="neutral"
          variant="subtle"
          :label="`${formatNumber(candidatesTotal)} total`"
        />
      </div>
    </template>

    <USkeleton v-if="pending" class="h-[220px] w-full" />
    <div
      v-else-if="candidatesTotal === 0"
      class="h-[220px] flex items-center justify-center text-sm text-muted"
    >
      No candidates yet.
    </div>
    <div v-else class="space-y-4">
      <div class="flex flex-wrap gap-2">
        <UBadge
          v-for="bucket in candidateBuckets"
          :key="bucket.label"
          :color="bucket.color"
          variant="subtle"
        >
          {{ bucket.label }}: {{ formatNumber(bucket.count) }}
        </UBadge>
      </div>
      <ChartsBarChart
        :data="candidateChartData"
        :height="candidateChartHeight"
        :categories="candidateChartCategories"
        :y-axis="['count']"
        :y-num-ticks="candidateChartData.length"
        :x-formatter="formatCount"
        :y-formatter="candidateLabelFormatter"
        :tooltip-title-formatter="labelTooltipTitle"
        v-bind="horizontalBarProps(96)"
      />
    </div>
  </UCard>
</template>
