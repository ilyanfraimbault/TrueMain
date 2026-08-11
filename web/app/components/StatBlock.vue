<script setup lang="ts">
/**
 * A measurement and what it measures — the most repeated motif on the site
 * (`52% / WR`, `2.4 / KDA`, `18 / games`, `100 / Games used`), until now
 * rewritten by hand in the champion list, the leaderboard, the match rows, the
 * profile cards and the recommendation strip.
 *
 * The point is the *gap* between the two lines. The old hand-written pairs put
 * a value and its label one step apart (`text-sm` over `text-xs`, same family,
 * same weight), which made a dense row read as undifferentiated noise. Here the
 * value is Geist Mono at a real display step and the label is a 10px uppercase
 * micro-label — see `stat-value` / `stat-label` in main.css, which own the
 * family, weight and figure style so a call site only picks the scale.
 *
 * Colour is opt-in and means one thing: where the value sits on the one-sided
 * data axis — rose gold above average, stepping down the neutral ramp below it.
 * `default` leaves the value at `text-highlighted`; use it for counts and
 * anything with no better/worse reading, because a number that is merely *large*
 * is not a number that is *good*.
 */
const props = withDefaults(defineProps<{
  /** Pre-formatted — this component never rounds, never adds a unit. */
  value: string | number
  label: string
  /** Secondary line under the pair (sample size, denominator, "avg similarity"). */
  caption?: string | null
  /**
   * Where the value sits on the data axis. `mid` is the deliberate "measured,
   * and it is average" — distinct from `default`, which is "no such reading
   * exists". An em dash for an unmeasured value should use `default`.
   */
  tone?: 'default' | 'good' | 'mid' | 'bad'
  /**
   * Escape hatch for callers that already own a *domain* colour rule and return
   * a Tailwind text class from it — `utils/rate-tone` is the one that matters:
   * a win rate must be banded identically wherever it appears, and re-deriving
   * `tone` from the same number at each call site is how that drifts. Wins over
   * `tone` when both are set.
   */
  valueClass?: string | null
  /** Display step of the value. The label stays 10px at every size. */
  size?: 'sm' | 'md' | 'lg' | 'xl'
  align?: 'start' | 'center' | 'end'
}>(), {
  caption: null,
  valueClass: null,
  tone: 'default',
  size: 'md',
  align: 'start',
})

// Literal class strings, never interpolated: Tailwind only emits utilities it
// can see in the source, so a computed `text-data-${tone}` would render as an
// unstyled element (see DESIGN_SYSTEM.md).
const TONE_CLASS = {
  default: '',
  good: 'text-data-good',
  mid: 'text-data-mid',
  bad: 'text-data-bad',
} as const

const SIZE_CLASS = {
  sm: 'text-sm',
  md: 'text-lg',
  lg: 'text-2xl',
  xl: 'text-4xl',
} as const

const ALIGN_CLASS = {
  start: 'items-start text-left',
  center: 'items-center text-center',
  end: 'items-end text-right',
} as const

const toneClass = computed(() => props.valueClass ?? TONE_CLASS[props.tone])
const sizeClass = computed(() => SIZE_CLASS[props.size])
const alignClass = computed(() => ALIGN_CLASS[props.align])
</script>

<template>
  <div
    class="flex min-w-0 flex-col gap-0.5"
    :class="alignClass"
  >
    <!-- Value first in the DOM as well as on screen: it is the answer, the
         label is the question. A screen reader reading "52% win rate" beats
         "win rate, 52%" for the same reason the eye prefers it. -->
    <span
      class="stat-value leading-tight"
      :class="[sizeClass, toneClass]"
    >
      {{ value }}
    </span>
    <span class="stat-label">{{ label }}</span>
    <span
      v-if="caption"
      class="truncate text-xs text-dimmed"
    >
      {{ caption }}
    </span>
  </div>
</template>
