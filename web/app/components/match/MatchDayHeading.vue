<script setup lang="ts">
// Date separator between two runs of same-day rows in a match history.
// Deliberately quieter than the section's own <h2>, and not uppercased like
// it: this is a date ("11 Aug"), and small-caps dates read as labels rather
// than as the thing they are. The trailing rule carries the eye across the
// row's full width so the break reads as a break even where the label is
// six characters wide.
defineProps<{ label: string }>()
</script>

<template>
  <h3 class="flex items-center gap-2 pt-1 text-xs font-semibold text-muted">
    {{ label }}
    <span class="h-0 flex-1 border-t border-default/60" aria-hidden="true" />
  </h3>
</template>
