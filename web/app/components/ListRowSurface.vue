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
</script>

<template>
  <div class="surface surface-hover flex items-center rounded-lg px-3 py-2.5">
    <slot />
  </div>
</template>
