<script setup lang="ts">
// The ten players' curves (#2102). Coordinates arrive in the plot's own units —
// x and y both 0..100 — and the SVG stretches to its box; strokes keep their
// width. The lines arrive in drawing order: the muted ones first, the pinned
// one over them, the viewer's own last, on top.
defineProps<{
  lines: {
    key: number
    points: string
    /** `self` is the viewer's own line, in the primary colour. */
    tone: 'self' | 'ally' | 'enemy'
    emphasis: 'self' | 'pinned' | 'muted'
  }[]
  /** The hovered frame's x, or null. */
  hoverX: number | null
  summary: string
}>()

const STROKES = {
  self: 'stroke-primary',
  ally: 'stroke-ally',
  enemy: 'stroke-enemy',
} as const

const WIDTHS = { self: 2.5, pinned: 2, muted: 1.25 } as const
</script>

<template>
  <svg
    viewBox="0 0 100 100"
    preserveAspectRatio="none"
    class="absolute inset-0 size-full overflow-visible"
    role="img"
    :aria-label="summary"
  >
    <line
      v-for="y in [0, 50]"
      :key="y"
      x1="0"
      x2="100"
      :y1="y"
      :y2="y"
      class="stroke-[var(--ui-border)]"
      vector-effect="non-scaling-stroke"
    />
    <line
      x1="0"
      x2="100"
      y1="100"
      y2="100"
      class="stroke-[var(--ui-border-accented)]"
      vector-effect="non-scaling-stroke"
    />
    <polyline
      v-for="line in lines"
      :key="line.key"
      :points="line.points"
      fill="none"
      :class="[STROKES[line.tone], line.emphasis === 'muted' ? 'opacity-40' : 'opacity-100']"
      :stroke-width="WIDTHS[line.emphasis]"
      stroke-linejoin="round"
      stroke-linecap="round"
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
