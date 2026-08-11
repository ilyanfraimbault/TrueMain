<script setup lang="ts">
/**
 * A rate as a length, not just a number. Pick rate, ban rate and presence are
 * read by *comparison* — "which of these is the popular one" — and a column of
 * bare percentages makes the eye do that comparison arithmetically, one row at
 * a time. A bar answers it in one pass down the column, which is what makes a
 * dense stats table scannable.
 *
 * Deliberately not a chart: no axis, no ticks, no library. It is a 4px rule
 * whose length carries the value, meant to sit *beside* the number rather than
 * replace it — the number stays the precise read.
 */
const props = withDefaults(defineProps<{
  /** The measurement, in the same unit as `max`. */
  value: number
  /**
   * What a full bar means. Rates come in as 0..1, so the default is 1 — but a
   * column of pick rates that all sit under 8% is unreadable against a 100%
   * track, so pass the column's own maximum to normalise against it. The number
   * beside the bar is what stays absolute.
   */
  max?: number
  tone?: 'good' | 'mid' | 'bad' | 'neutral'
  /** Accessible name — the bar is `role="img"`, not a live progress bar. */
  label: string
}>(), {
  max: 1,
  tone: 'neutral',
})

// Literal strings, never interpolated — Tailwind only emits what it can see.
const TONE_CLASS = {
  good: 'bg-data-good',
  mid: 'bg-data-mid',
  bad: 'bg-data-bad',
  neutral: 'bg-data-mid',
} as const

/**
 * Clamped, and guarded against a zero or negative `max`: a column whose values
 * are all zero would otherwise divide by zero and render `NaN%`, which CSS
 * drops silently — leaving a full-width bar on an empty column.
 */
const percent = computed(() => {
  const max = props.max > 0 ? props.max : 1
  const ratio = props.value / max
  if (!Number.isFinite(ratio)) return 0
  return Math.min(100, Math.max(0, ratio * 100))
})

const toneClass = computed(() => TONE_CLASS[props.tone])
</script>

<template>
  <span
    class="block h-1 w-full overflow-hidden rounded-full bg-muted"
    role="img"
    :aria-label="label"
  >
    <!-- Width is an inline style, not a class: it is a value, and Tailwind
         cannot emit a utility per percentage. -->
    <span
      class="block h-full rounded-full"
      :class="toneClass"
      :style="{ width: `${percent}%` }"
    />
  </span>
</template>
