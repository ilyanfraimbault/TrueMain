<script setup lang="ts">
import type { PlayerLane } from '~/utils/recording-moments'
import { LANE_NAMES, PLAYER_LANES } from '~/utils/recording-moments'
import { TIMELINE_CURVE_HEIGHT, TIMELINE_LANE_HEIGHT, TIMELINE_LANES_HEIGHT, TIMELINE_PANEL_TOP, timelineLaneCenter } from '~/utils/recording-timeline'

/** The timeline's lanes named: each player lane's glyph beside it, and the curve's when the game has one. */
defineProps<{
  /** The track's height, so each glyph sits beside its lane. */
  height: string
  curve: boolean
}>()

const LANE_TONES: Record<PlayerLane, string> = { kill: 'text-data-good', assist: 'text-ink-400', death: 'text-red-400' }
</script>

<template>
  <div class="relative w-4 shrink-0" :style="{ height }">
    <div
      v-for="lane in PLAYER_LANES"
      :key="lane"
      class="absolute inset-x-0 flex items-center justify-center"
      :style="{ top: `${timelineLaneCenter(lane) - TIMELINE_LANE_HEIGHT / 2}px`, height: `${TIMELINE_LANE_HEIGHT}px` }"
      :title="LANE_NAMES[lane]"
    >
      <RecapMomentIcon :kind="lane" class="size-3.5" :class="LANE_TONES[lane]" />
    </div>
    <div
      v-if="curve"
      class="absolute inset-x-0 flex items-center justify-center"
      :style="{ top: `${TIMELINE_PANEL_TOP + TIMELINE_LANES_HEIGHT}px`, height: `${TIMELINE_CURVE_HEIGHT}px` }"
      title="Win probability"
    >
      <UIcon name="i-lucide-chart-spline" class="size-3.5 text-ally" />
    </div>
  </div>
</template>
