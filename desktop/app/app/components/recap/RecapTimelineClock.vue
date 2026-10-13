<script setup lang="ts">
import type { TimelineView } from '~/utils/recording-timeline'
import { formatClock } from '~/utils/recording-moments'
import { viewPercent } from '~/utils/recording-timeline'

/** The clock under the timeline: the window's tick labels, and the time under the pointer. */
defineProps<{
  ticks: number[]
  view: TimelineView
  hoverMs: number | null
}>()
</script>

<template>
  <div class="relative ml-6 mt-1 h-3 text-[10px] tabular-nums text-dimmed">
    <span v-for="at in ticks" :key="at" class="absolute -translate-x-1/2" :class="hoverMs !== null && 'opacity-40'" :style="{ left: viewPercent(view, at) }">{{ formatClock(at) }}</span>
    <span
      v-if="hoverMs !== null"
      class="pointer-events-none absolute -translate-x-1/2 rounded bg-ink-950 px-1 text-highlighted"
      :style="{ left: viewPercent(view, hoverMs) }"
    >{{ formatClock(hoverMs) }}</span>
  </div>
</template>
