<script setup lang="ts">
import { formatGameClock } from '#common/utils/win-probability-swing'

// Under the win-probability curve: the event ticks — turning points by side,
// the other monsters neutral — then the minute labels. Every `x` is in % of the
// plot; while a minute is hovered, the ticks outside it fade.
defineProps<{
  swings: { index: number, ms: number, x: number, label: string, ours: boolean, lit: boolean }[]
  objectives: { ms: number, x: number, label: string }[]
  /** Whether a hover is lighting some ticks (the rest then fade). */
  hovering: boolean
  minutes: { minute: number, x: number }[]
}>()
</script>

<template>
  <div>
    <div class="relative mt-1 h-3">
      <span
        v-for="swing in swings"
        :key="`tick-${swing.index}`"
        class="absolute top-0 h-3 w-0.5 -translate-x-1/2 rounded-full transition-opacity"
        :class="[swing.ours ? 'bg-ally' : 'bg-enemy', !hovering || swing.lit ? 'opacity-100' : 'opacity-30']"
        :style="{ left: `${swing.x}%` }"
        :title="`${formatGameClock(swing.ms)} · ${swing.label}`"
      />
      <span
        v-for="objective in objectives"
        :key="`objective-${objective.ms}`"
        class="absolute top-1 size-1.5 -translate-x-1/2 rounded-full bg-[var(--ui-text-dimmed)]"
        :style="{ left: `${objective.x}%` }"
        :title="`${formatGameClock(objective.ms)} · ${objective.label}`"
      />
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
