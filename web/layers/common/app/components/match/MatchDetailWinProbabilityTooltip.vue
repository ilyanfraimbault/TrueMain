<script setup lang="ts">
import { formatPercentage } from '#shared/utils/ddragon'
import { formatGameClock, formatSwingPoints } from '#common/utils/win-probability-swing'

// What a hovered minute of the win-probability curve says: the clock and the
// chance, then that stretch's turning points (by side, with their swing) and
// the monsters no turning point already marks.
defineProps<{
  ms: number
  /** The side's chance at that point, 0..1. */
  chance: number
  /** Horizontal anchor, in % of the plot. */
  left: number
  swings: { index: number, label: string, ours: boolean, delta: number }[]
  objectives: { ms: number, label: string }[]
}>()
</script>

<template>
  <div
    class="pointer-events-none absolute top-0 z-10 flex w-max max-w-72 -translate-x-1/2 flex-col gap-1 rounded-md bg-default px-2.5 py-1.5 text-xs shadow-lg ring-1 ring-default"
    :style="{ left: `${left}%` }"
  >
    <span class="tabular-nums">
      <span class="text-muted">{{ formatGameClock(ms) }}</span>
      <span class="ml-1.5 font-semibold text-highlighted">{{ formatPercentage(chance, 0) }}</span>
    </span>
    <span v-for="swing in swings" :key="`hover-swing-${swing.index}`" class="flex items-center gap-2">
      <span class="size-1.5 shrink-0 rounded-full" :class="swing.ours ? 'bg-ally' : 'bg-enemy'" />
      <span class="min-w-0 flex-1 text-default">{{ swing.label }}</span>
      <span class="shrink-0 font-semibold tabular-nums text-highlighted">{{ formatSwingPoints(swing.delta) }}<span class="font-normal text-dimmed"> pts</span></span>
    </span>
    <span v-for="objective in objectives" :key="`hover-objective-${objective.ms}`" class="flex items-center gap-2">
      <span class="size-1.5 shrink-0 rounded-full bg-[var(--ui-text-dimmed)]" />
      <span class="text-muted">{{ objective.label }}</span>
    </span>
  </div>
</template>
