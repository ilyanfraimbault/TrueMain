<script setup lang="ts">
import type { ObjectiveMarker } from '#common/utils/timeline-objectives'
import { formatGameClock } from '#common/utils/win-probability-swing'

// Under the gold / XP / CS curves and their lead band (#2102): the game's
// objectives as glyphs in the colour of the side that took them, then the
// minute labels. Every `x` is in % of the plot.
defineProps<{
  markers: (ObjectiveMarker & { x: number, ours: boolean })[]
  minutes: { minute: number, x: number }[]
}>()
</script>

<template>
  <div>
    <div class="relative mt-1 h-4">
      <span
        v-for="marker in markers"
        :key="`objective-${marker.kind}-${marker.ms}`"
        class="absolute top-0 flex -translate-x-1/2 items-center"
        :class="marker.ours ? 'text-ally' : 'text-enemy'"
        :style="{ left: `${marker.x}%` }"
        :title="`${formatGameClock(marker.ms)} · ${marker.label}`"
      >
        <MatchDetailObjectiveIcon
          :kind="marker.kind"
          :class="marker.kind === 'tower' ? 'size-3' : 'size-3.5'"
          :aria-label="`${formatGameClock(marker.ms)} · ${marker.label}`"
        />
      </span>
    </div>
    <div class="relative h-4 text-[10px] tabular-nums text-dimmed">
      <span
        v-for="tick in minutes"
        :key="tick.minute"
        class="absolute -translate-x-1/2"
        :style="{ left: `${tick.x}%` }"
      >{{ tick.minute }}'</span>
    </div>
  </div>
</template>
