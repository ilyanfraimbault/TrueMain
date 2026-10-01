<script setup lang="ts">
/**
 * One metric game by game, oldest to newest: the shape of a run of form, with
 * no axis — the tile above it carries the number. Stretches to its box.
 */
const props = withDefaults(defineProps<{
  values: number[]
  /** `good` / `bad` follow the data axis; `flat` is a trend worth no colour. */
  tone?: 'good' | 'bad' | 'flat'
}>(), { tone: 'flat' })

const id = useId()
const WIDTH = 100
const HEIGHT = 24
const PAD = 2

const line = computed(() => {
  const values = props.values
  if (values.length < 2) return ''
  const min = Math.min(...values)
  const span = Math.max(...values) - min || 1
  return values
    .map((value, index) => {
      const x = (index / (values.length - 1)) * WIDTH
      const y = PAD + (1 - (value - min) / span) * (HEIGHT - PAD * 2)
      return `${index ? 'L' : 'M'}${x.toFixed(2)},${y.toFixed(2)}`
    })
    .join('')
})

const stroke = computed(() => ({
  good: 'var(--color-data-good)',
  bad: 'var(--color-ink-400)',
  flat: 'var(--color-ink-300)',
}[props.tone]))
</script>

<template>
  <svg
    v-if="line"
    :viewBox="`0 0 ${WIDTH} ${HEIGHT}`"
    preserveAspectRatio="none"
    class="block h-6 w-full overflow-visible"
    aria-hidden="true"
  >
    <defs>
      <linearGradient :id="`${id}-fill`" x1="0" x2="0" y1="0" y2="1">
        <stop offset="0%" :stop-color="stroke" stop-opacity="0.22" />
        <stop offset="100%" :stop-color="stroke" stop-opacity="0" />
      </linearGradient>
    </defs>
    <path :d="`${line}L${WIDTH},${HEIGHT}L0,${HEIGHT}Z`" :fill="`url(#${id}-fill)`" />
    <path :d="line" fill="none" :stroke="stroke" stroke-width="1.5" stroke-linejoin="round" vector-effect="non-scaling-stroke" />
  </svg>
  <div v-else class="h-6" />
</template>
