<script setup lang="ts">
// A run's gap from its recent median (#2298), drawn only once the API flagged it
// (past the thresholds, with enough history). The tooltip names the median it was
// read against, formatted like the value itself.
import type { ProcessRunMetricBaseline } from '~~/shared/types/ops'
import { computed } from 'vue'
import { formatElapsed, formatNumber } from '~~/shared/utils/format'
import { flaggedMetric, formatDeviation } from '~~/shared/utils/process-baseline'

const props = defineProps<{
  metric: ProcessRunMetricBaseline | null | undefined
  sampleCount: number
  /** `duration` formats the median as elapsed time, `count` as a number. */
  kind: 'duration' | 'count'
}>()

const flagged = computed(() => flaggedMetric(props.metric))
const tooltip = computed(() => {
  if (!flagged.value) {
    return ''
  }
  const median = props.kind === 'duration'
    ? formatElapsed(flagged.value.median)
    : formatNumber(flagged.value.median)
  return `Median of the last ${props.sampleCount} successful runs: ${median}`
})
</script>

<template>
  <UTooltip v-if="flagged" :text="tooltip">
    <UBadge
      color="warning"
      variant="subtle"
      size="sm"
      class="tabular-nums whitespace-nowrap"
      :label="`${formatDeviation(flagged.deviation)} vs median`"
    />
  </UTooltip>
</template>
