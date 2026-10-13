<script setup lang="ts">
// The win-probability plot itself: the chance above even tinted ally, below it
// enemy, the even line dashed, the hovered turning points as vertical rules.
// Coordinates arrive in the plot's own units — x and y both 0..100 — and the
// SVG stretches to its box; strokes keep their width.
const props = defineProps<{
  points: { x: number, y: number }[]
  /** Turning points as rules: shown only while `lit`. */
  rules: { key: number, x: number, ours: boolean, lit: boolean }[]
  /** The hovered point's x, or null. */
  hoverX: number | null
  summary: string
}>()

const MID = 50
// Each open match draws its own clips: two panels open side by side must not share an id.
const clipId = `wp-${useId()}`

const line = computed(() => props.points.map(point => `${point.x},${point.y}`).join(' '))
const area = computed(() => {
  const first = props.points[0]
  const last = props.points.at(-1)
  if (!first || !last) return ''
  return `M${first.x},${MID} L${line.value.replaceAll(' ', ' L')} L${last.x},${MID} Z`
})
</script>

<template>
  <svg
    viewBox="0 0 100 100"
    preserveAspectRatio="none"
    class="absolute inset-0 size-full overflow-visible"
    role="img"
    :aria-label="summary"
  >
    <defs>
      <clipPath :id="`${clipId}-ahead`"><rect x="0" y="0" width="100" :height="MID" /></clipPath>
      <clipPath :id="`${clipId}-behind`"><rect x="0" :y="MID" width="100" :height="MID" /></clipPath>
    </defs>
    <line
      v-for="y in [75, 25]"
      :key="y"
      x1="0"
      x2="100"
      :y1="y"
      :y2="y"
      class="stroke-[var(--ui-border)]"
      vector-effect="non-scaling-stroke"
    />
    <path :d="area" class="fill-ally/20" :clip-path="`url(#${clipId}-ahead)`" />
    <path :d="area" class="fill-enemy/20" :clip-path="`url(#${clipId}-behind)`" />
    <line
      x1="0"
      x2="100"
      :y1="MID"
      :y2="MID"
      class="stroke-[var(--ui-text-dimmed)]"
      stroke-dasharray="3 3"
      vector-effect="non-scaling-stroke"
    />
    <polyline
      :points="line"
      fill="none"
      class="stroke-[var(--ui-text-highlighted)]"
      stroke-width="2"
      stroke-linejoin="round"
      vector-effect="non-scaling-stroke"
    />
    <line
      v-for="rule in rules"
      :key="`swing-${rule.key}`"
      :x1="rule.x"
      :x2="rule.x"
      y1="0"
      y2="100"
      :class="rule.lit ? (rule.ours ? 'stroke-ally' : 'stroke-enemy') : 'stroke-transparent'"
      stroke-width="1.5"
      vector-effect="non-scaling-stroke"
    />
    <line
      v-if="hoverX !== null"
      :x1="hoverX"
      :x2="hoverX"
      y1="0"
      y2="100"
      class="stroke-[var(--ui-text-dimmed)]"
      vector-effect="non-scaling-stroke"
    />
  </svg>
</template>
