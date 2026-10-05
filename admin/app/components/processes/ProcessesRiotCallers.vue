<script setup lang="ts">
// Riot API tab: consumption by caller (#1035), a horizontal categorical bar chart of
// who is spending the budget. Follows the same `horizontalBarProps`/`barChartHeight`
// pattern as the top-tables / top-champions charts elsewhere in the admin.
import type { RiotCallerUsage } from '~~/shared/types/ops'

const props = defineProps<{
  callerBreakdown: RiotCallerUsage[]
  pending: boolean
}>()

const callerChartData = computed(() =>
  props.callerBreakdown.map(row => ({ label: row.caller, calls: row.calls })),
)
const callerChartHeight = computed(() =>
  barChartHeight(callerChartData.value.length, { min: 120, step: 32 }),
)
const callerCategories = { calls: { name: 'Calls', color: CHART_PRIMARY } }
const callerLabelFormatter = computed(() =>
  indexLabelFormatter(callerChartData.value, row => row.label),
)
</script>

<template>
  <UCard class="mb-6">
    <template #header>
      <PanelTitle variant="label" title="Consumption by caller" />
    </template>

    <USkeleton v-if="pending" :style="{ height: `${callerChartHeight}px` }" class="w-full" />
    <div
      v-else-if="callerChartData.length === 0"
      class="flex h-[120px] items-center justify-center text-sm text-muted"
    >
      No calls recorded in this window.
    </div>
    <ChartsBarChart
      v-else
      :data="callerChartData"
      :height="callerChartHeight"
      :categories="callerCategories"
      :y-axis="['calls']"
      :y-num-ticks="callerChartData.length"
      :x-formatter="formatCount"
      :y-formatter="callerLabelFormatter"
      :tooltip-title-formatter="labelTooltipTitle"
      v-bind="horizontalBarProps(120)"
    />
  </UCard>
</template>
