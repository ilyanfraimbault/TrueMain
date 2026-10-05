<script setup lang="ts">
import type { GameState } from '~/types/game'
import type { PaceComparison, PaceReference, PaceStanding } from '~/utils/pace-benchmark'
import { tierLabel } from '~/utils/pace-benchmark'

/**
 * Our pace: CS per minute, with its curve so a glance says whether it is
 * climbing or slipping, and gold per minute. From the samples the shell
 * keeps at each whole minute (`live_client::pace`); gold is gold earned as
 * far as the API lets it be read (inventory cost plus gold in hand).
 *
 * With a `reference` (#1912), each figure gains the median of games played at
 * our Solo/Duo tier at this minute, an arrow in the data colours for where we
 * stand against that tier's quartiles, and the curve a faint line of the
 * median. A reference, not advice: nothing here tells the player what to do.
 * Our gold reads a little under gold earned (consumables used and sell-back
 * losses are invisible to the API), so the gold arrow leans low, never high.
 */
const props = defineProps<{ game: GameState, reference?: PaceReference | null }>()

const samples = computed(() => props.game.pace.samples)
const last = computed(() => samples.value.at(-1) ?? null)

const csPerMinute = computed(() => (last.value ? last.value.cs / last.value.minute : 0))
const goldPerMinute = computed(() => (last.value ? Math.round(last.value.gold / last.value.minute) : 0))

const tier = computed(() => (props.reference ? tierLabel(props.reference.tier) : null))

const STANDING: Record<PaceStanding, { icon: string, tone: string }> = {
  above: { icon: 'i-lucide-arrow-up', tone: 'text-data-good' },
  within: { icon: 'i-lucide-equal', tone: 'text-data-mid' },
  below: { icon: 'i-lucide-arrow-down', tone: 'text-data-bad' },
}

const comparisons = computed(() => {
  const reference = props.reference
  const row = (comparison: PaceComparison | null | undefined, digits: number) => comparison
    ? { ...STANDING[comparison.standing], median: comparison.median.toFixed(digits) }
    : null
  return { cs: row(reference?.cs, 1), gold: row(reference?.gold, 0) }
})

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
  const lastMinute = points.at(-1)!.minute
  const median = (props.reference?.csCurve ?? [])
    .filter(point => point.minute >= first && point.minute <= lastMinute)
  const span = Math.max(1, lastMinute - first)
  // One scale for both lines, so the reference sits where its values are.
  const values = [...points, ...median].map(point => point.value)
  const low = Math.min(...values)
  const high = Math.max(...values)
  const range = high - low || 1
  const toLine = (line: { minute: number, value: number }[]) => line.map(point => ({
    x: ((point.minute - first) / span) * WIDTH,
    // A flat curve sits in the middle; otherwise it fills the height with a margin for the stroke.
    y: high === low ? HEIGHT / 2 : 2 + (1 - (point.value - low) / range) * (HEIGHT - 4),
  }))
  const join = (xy: { x: number, y: number }[]) => xy.map(point => `${point.x.toFixed(1)},${point.y.toFixed(1)}`).join(' ')
  const xy = toLine(points)
  const line = join(xy)
  return {
    line,
    area: `0,${HEIGHT} ${line} ${WIDTH},${HEIGHT}`,
    end: xy.at(-1)!,
    median: median.length >= 2 ? join(toLine(median)) : null,
  }
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
        <polyline v-if="curve.median" :points="curve.median" fill="none" class="text-data-mid" stroke="currentColor" stroke-opacity="0.6" stroke-width="1" stroke-dasharray="2 2" />
        <polyline :points="curve.line" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linejoin="round" stroke-linecap="round" />
        <circle :cx="curve.end.x" :cy="curve.end.y" r="2" fill="currentColor" />
      </svg>
    </div>
    <span v-if="tier && comparisons.cs" class="-mt-1 flex items-center gap-1 whitespace-nowrap text-[10px] leading-none tabular-nums text-muted">
      <UIcon :name="comparisons.cs.icon" :class="['size-2.5 shrink-0', comparisons.cs.tone]" />
      {{ tier }} {{ comparisons.cs.median }}
    </span>
    <div class="flex flex-col border-t border-default pt-2">
      <span class="text-[10px] font-semibold uppercase tracking-wider text-dimmed">Gold/min</span>
      <span class="stat-value text-xl leading-none tabular-nums text-stat-gold">{{ last ? goldPerMinute : '–' }}</span>
      <span v-if="tier && comparisons.gold" class="mt-1 flex items-center gap-1 whitespace-nowrap text-[10px] leading-none tabular-nums text-muted">
        <UIcon :name="comparisons.gold.icon" :class="['size-2.5 shrink-0', comparisons.gold.tone]" />
        {{ tier }} {{ comparisons.gold.median }}
      </span>
    </div>
  </div>
</template>
