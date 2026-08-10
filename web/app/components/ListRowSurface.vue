<script setup lang="ts">
// Shared row surface for the champion list and the truemain leaderboard: same
// surface-hover background, border, radius and padding so both lists read as
// one visual system. Layout (gap, cursor, focus ring, @container) stays with
// each caller since the two rows lay out their columns differently.
</script>

<template>
  <div class="surface-hover flex items-center rounded-lg border border-default/60 bg-elevated/60 px-3 py-2.5">
    <slot />
  </div>
</template>
