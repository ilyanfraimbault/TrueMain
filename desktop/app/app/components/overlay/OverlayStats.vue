<script setup lang="ts">
import type { GameState } from '~/types/game'

/**
 * Our pace: CS per minute, with its curve so a glance says whether it is
 * climbing or slipping, and gold per minute. From the samples the shell
 * keeps at each whole minute (`live_client::pace`); gold is gold earned as
 * far as the API lets it be read (inventory cost plus gold in hand).
 */
const props = defineProps<{ game: GameState }>()

const samples = computed(() => props.game.pace.samples)
const last = computed(() => samples.value.at(-1) ?? null)

const csPerMinute = computed(() => (last.value ? last.value.cs / last.value.minute : 0))
const goldPerMinute = computed(() => (last.value ? Math.round(last.value.gold / last.value.minute) : 0))

/** The curve leaves out the first minutes, when no minion has reached a lane yet and every value is a ramp. */
const CURVE_FROM = 3
const WIDTH = 72
const HEIGHT = 26

const curve = computed(() => {
  const points = samples.value
    .filter(sample => sample.minute >= CURVE_FROM)
    .map(sample => ({ minute: sample.minute, value: sample.cs / sample.minute }))
  if (points.length < 2) return null
  const first = points[0]!.minute
  const span = Math.max(1, points.at(-1)!.minute - first)
  const low = Math.min(...points.map(point => point.value))
  const high = Math.max(...points.map(point => point.value))
  const range = high - low || 1
  const xy = points.map(point => ({
    x: ((point.minute - first) / span) * WIDTH,
    // A flat curve sits in the middle; otherwise it fills the height with a margin for the stroke.
    y: high === low ? HEIGHT / 2 : 2 + (1 - (point.value - low) / range) * (HEIGHT - 4),
  }))
  const line = xy.map(point => `${point.x.toFixed(1)},${point.y.toFixed(1)}`).join(' ')
  return { line, area: `0,${HEIGHT} ${line} ${WIDTH},${HEIGHT}`, end: xy.at(-1)! }
})

const gradient = `pace-${useId()}`
</script>

<template>
  <div class="flex flex-col gap-2">
    <div class="flex items-end justify-between gap-3">
      <div class="flex flex-col">
        <span class="text-[10px] font-semibold uppercase tracking-wider text-dimmed">CS/min</span>
        <span class="stat-value text-xl leading-none tabular-nums text-highlighted">{{ last ? csPerMinute.toFixed(1) : '–' }}</span>
      </div>
      <svg v-if="curve" :width="WIDTH" :height="HEIGHT" :viewBox="`0 0 ${WIDTH} ${HEIGHT}`" class="overflow-visible text-primary" aria-hidden="true">
        <defs>
          <linearGradient :id="gradient" x1="0" y1="0" x2="0" y2="1">
            <stop offset="0" stop-color="currentColor" stop-opacity="0.28" />
            <stop offset="1" stop-color="currentColor" stop-opacity="0" />
          </linearGradient>
        </defs>
        <polygon :points="curve.area" :fill="`url(#${gradient})`" />
        <polyline :points="curve.line" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linejoin="round" stroke-linecap="round" />
        <circle :cx="curve.end.x" :cy="curve.end.y" r="2" fill="currentColor" />
      </svg>
    </div>
    <div class="flex flex-col border-t border-default pt-2">
      <span class="text-[10px] font-semibold uppercase tracking-wider text-dimmed">Gold/min</span>
      <span class="stat-value text-xl leading-none tabular-nums text-stat-gold">{{ last ? goldPerMinute : '–' }}</span>
    </div>
  </div>
</template>
