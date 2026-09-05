<script setup lang="ts">
// Shared row surface for the champion list and the truemain leaderboard: same
// material, radius and padding so both lists read as one visual system. Layout
// (gap, cursor, focus ring, @container) stays with each caller since the two
// rows lay out their columns differently.
//
// The fill and hairline come from `surface` alone. Spelling them out again as
// `bg-elevated/60 border-default/60` would not merely be redundant — a plain
// utility out-cascades a `@utility` declaration, so the literal pair wins and
// the row goes translucent again.
//
// Padding is a prop rather than a class the caller passes: a fallthrough
// `py-0` and the component's own `py-2.5` have the same specificity, so which
// one wins depends on the order Tailwind emits them, not on the call site.
const { dense = false } = defineProps<{
  /**
   * Collapses the vertical padding and tightens the horizontal one, so the row
   * is exactly as tall as the tallest thing in it (the 40px avatar on a
   * leaderboard row) instead of that plus 20px of air. Used by the lists whose
   * rows are dense enough that the padding was the dominant part of the height.
   */
  dense?: boolean
}>()
</script>

<template>
  <div
    class="surface surface-hover flex items-center rounded-lg"
    :class="dense ? 'px-1.5 py-0' : 'px-3 py-2.5'"
  >
    <slot />
  </div>
</template>
