<script setup lang="ts">
import type { CrestBand } from '#common/utils/rank-chart'
import { scoreTopPx } from '#common/utils/rank-chart'

// The LP curve's y-axis: one tier crest per tier in the history, stacked at
// its score band. The column shares the chart's exact height so the offsets
// line up with the data range.
defineProps<{
  bands: readonly CrestBand[]
  yDomain: [number, number]
  height: number
}>()
</script>

<template>
  <div
    class="relative w-7 shrink-0"
    :style="{ height: `${height}px` }"
    aria-hidden="true"
  >
    <RankIcon
      v-for="band in bands"
      :key="band.tier"
      :tier="band.tier"
      :size="20"
      class="absolute left-0 -translate-y-1/2"
      :style="{ top: `${scoreTopPx(band.score, yDomain, height)}px` }"
    />
  </div>
</template>
