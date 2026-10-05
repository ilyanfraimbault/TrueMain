<script setup lang="ts">
import type { RecapCurvePoint, RecapTurningPoint } from '~/utils/recording-win-probability'
import { formatGameClock, formatSwingPoints } from '#common/utils/win-probability-swing'
import { curveAt } from '~/utils/recording-win-probability'

/**
 * The recap timeline's win-probability lane (#1911): the player's side's
 * chance across the video, the half above even tinted in the ally's blue and
 * the half below in the enemy's red, and the turning points as dots on the
 * line — a click on one seeks a few seconds before it, like a moment. Drawn in
 * the lane's own box, positioned by its parent; everything but the dots lets
 * the pointer through to the track, so a click seeks and a drag cuts a range
 * across this lane as across the others.
 */
const props = defineProps<{
  curve: RecapCurvePoint[]
  turningPoints: RecapTurningPoint[]
  lengthMs: number
  /** The lane's height, px. */
  height: number
}>()
const emit = defineEmits<{ jump: [point: RecapTurningPoint] }>()

/** The SVG's own units: the video's length across, 100 down — stretched to the lane. */
const width = computed(() => Math.max(props.lengthMs, 1))
const x = (videoMs: number) => Math.min(Math.max(videoMs, 0), width.value)
const y = (p: number) => (1 - p) * 100

const line = computed(() => props.curve.map((point, index) => `${index ? 'L' : 'M'}${x(point.videoMs)},${y(point.p)}`).join(' '))
/** The area between the line and even: clipped to each half, it is the ally's lead above and the enemy's below. */
const area = computed(() => {
  const first = props.curve[0]
  const last = props.curve[props.curve.length - 1]
  if (!first || !last) return ''
  return `M${x(first.videoMs)},50 ${line.value.replace(/^M/, 'L')} L${x(last.videoMs)},50 Z`
})

const id = useId()
const pct = (ms: number) => `${(x(ms) / width.value) * 100}%`
const dots = computed(() => [...props.turningPoints]
  // The smallest drawn last, so a big swing never hides a small one beside it.
  .sort((a, b) => Math.abs(b.delta) - Math.abs(a.delta))
  .map(point => ({ point, top: (y(curveAt(props.curve, point.videoMs)) / 100) * props.height })))
</script>

<template>
  <div class="pointer-events-none relative size-full">
    <svg class="absolute inset-0 size-full overflow-visible" :viewBox="`0 0 ${width} 100`" preserveAspectRatio="none" aria-hidden="true">
      <defs>
        <clipPath :id="`${id}-ally`"><rect x="0" y="0" :width="width" height="50" /></clipPath>
        <clipPath :id="`${id}-enemy`"><rect x="0" y="50" :width="width" height="50" /></clipPath>
      </defs>
      <line x1="0" :x2="width" y1="50" y2="50" class="stroke-white/15" stroke-dasharray="3 3" vector-effect="non-scaling-stroke" />
      <path :d="area" class="fill-ally/20" :clip-path="`url(#${id}-ally)`" />
      <path :d="area" class="fill-enemy/20" :clip-path="`url(#${id}-enemy)`" />
      <path :d="line" class="fill-none stroke-ink-200" stroke-width="1.5" stroke-linejoin="round" vector-effect="non-scaling-stroke" />
    </svg>

    <button
      v-for="({ point, top }, index) in dots"
      :key="index"
      type="button"
      class="pointer-events-auto absolute size-2.5 -translate-1/2 rounded-full ring-2 ring-(--ui-bg-muted) transition-transform hover:z-10 hover:scale-150"
      :class="point.delta >= 0 ? 'bg-ally' : 'bg-enemy'"
      :style="{ left: pct(point.videoMs), top: `${top}px` }"
      :title="`${point.label} · ${formatSwingPoints(point.delta)} · ${formatGameClock(point.gameMs)}`"
      :aria-label="`${point.label}, ${formatSwingPoints(point.delta)} points, at ${formatGameClock(point.gameMs)} of the game`"
      @pointerdown.stop
      @click="emit('jump', point)"
    />
  </div>
</template>
