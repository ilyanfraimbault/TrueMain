<script setup lang="ts">
import type { CrestBand } from '#common/utils/rank-chart-geometry'

// The LP curve's y-axis: one tier crest per tier band in view, at the middle of
// the band. The column shares the plot's exact height so the offsets line up.
defineProps<{
  crests: readonly CrestBand[]
  height: number
}>()
</script>

<template>
  <div
    class="relative w-7 shrink-0"
    :style="{ height: `${height}px` }"
    aria-hidden="true"
  >
    <!-- Positioned by a wrapper: RankIcon's own root is `relative`, which outranks an `absolute` passed to it. -->
    <div
      v-for="crest in crests"
      :key="crest.tier"
      class="absolute left-0 flex -translate-y-1/2"
      :style="{ top: `${crest.top}px` }"
    >
      <RankIcon :tier="crest.tier" :size="20" />
    </div>
  </div>
</template>
