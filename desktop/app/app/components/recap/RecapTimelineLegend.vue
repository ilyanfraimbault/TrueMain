<script setup lang="ts">
import { TIMELINE_CURVE_HEIGHT, TIMELINE_PANEL_TOP } from '~/utils/recording-timeline'

/** The timeline's rows named: the curve's glyph beside its lane when the game has one. Kept as a gutter without, so the clock lines up. */
defineProps<{
  /** The track's height. */
  height: string
  curve: boolean
}>()
</script>

<template>
  <div class="relative w-4 shrink-0" :style="{ height }">
    <div
      v-if="curve"
      class="absolute inset-x-0 flex items-center justify-center"
      :style="{ top: `${TIMELINE_PANEL_TOP}px`, height: `${TIMELINE_CURVE_HEIGHT}px` }"
      title="Win probability"
    >
      <UIcon name="i-lucide-chart-spline" class="size-3.5 text-ally" />
    </div>
  </div>
</template>
