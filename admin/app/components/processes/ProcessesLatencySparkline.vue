<script setup lang="ts">
// One route's p95 over the Performance tab's window (#2307): a line per run of measured
// bins, a dot for a bin measured alone, and a faint line at the slow threshold. An idle bin
// breaks the line — it was not fast, it was not called.
import type { RouteLatencyPoint } from '~~/shared/types/ops'
import { computed } from 'vue'
import { sparkline } from '~~/shared/utils/request-latency'

const props = withDefaults(defineProps<{
  series: RouteLatencyPoint[]
  thresholdMs: number
  slow?: boolean
  width?: number
  height?: number
}>(), { slow: false, width: 120, height: 24 })

const geometry = computed(() => sparkline(props.series, props.width, props.height, props.thresholdMs))
const lines = computed(() => geometry.value.segments
  .filter(segment => segment.length > 1)
  .map(segment => segment.map(point => `${point.x.toFixed(1)},${point.y.toFixed(1)}`).join(' ')))
const dots = computed(() => geometry.value.segments
  .filter(segment => segment.length === 1)
  .map(segment => segment[0]!))
</script>

<template>
  <svg
    :width="width"
    :height="height"
    :viewBox="`0 0 ${width} ${height}`"
    class="overflow-visible"
    :class="slow ? 'text-error' : 'text-primary'"
    role="img"
    aria-label="p95 over the window"
  >
    <line
      x1="0"
      :x2="width"
      :y1="geometry.thresholdY"
      :y2="geometry.thresholdY"
      class="stroke-(--ui-border-accented)"
      stroke-dasharray="2 2"
      stroke-width="1"
    />
    <polyline
      v-for="(points, index) in lines"
      :key="`line-${index}`"
      :points="points"
      fill="none"
      stroke="currentColor"
      stroke-width="1.5"
      stroke-linejoin="round"
      stroke-linecap="round"
    />
    <circle
      v-for="(point, index) in dots"
      :key="`dot-${index}`"
      :cx="point.x"
      :cy="point.y"
      r="1.5"
      fill="currentColor"
    />
  </svg>
</template>
