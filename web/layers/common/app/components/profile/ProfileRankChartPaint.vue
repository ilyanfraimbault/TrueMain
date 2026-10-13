<script setup lang="ts">
import type { GradientStop } from '#common/utils/rank-chart'

// Paint servers for the LP curve's line stroke and area fill: the tier
// gradient along x, and the area's vertical fade as a mask. They live in their
// own zero-sized SVG because the chart's own <defs> are owned by the upstream
// component; `url(#…)` references resolve document-wide, so they still apply
// inside the chart's SVG.
defineProps<{
  tierGradientId: string
  stops: readonly GradientStop[]
  areaFadeGradientId: string
  areaFadeMaskId: string
}>()
</script>

<template>
  <svg class="absolute size-0 overflow-hidden" aria-hidden="true" focusable="false">
    <defs>
      <linearGradient :id="tierGradientId" x1="0" y1="0" x2="1" y2="0">
        <stop
          v-for="(stop, i) in stops"
          :key="i"
          :offset="stop.offset"
          :stop-color="stop.color"
        />
      </linearGradient>
      <linearGradient :id="areaFadeGradientId" x1="0" y1="0" x2="0" y2="1">
        <stop offset="0%" stop-color="white" stop-opacity="0.45" />
        <stop offset="100%" stop-color="white" stop-opacity="0.05" />
      </linearGradient>
      <mask :id="areaFadeMaskId" maskContentUnits="objectBoundingBox">
        <rect x="0" y="0" width="1" height="1" :fill="`url(#${areaFadeGradientId})`" />
      </mask>
    </defs>
  </svg>
</template>
