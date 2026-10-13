<script setup lang="ts">
/**
 * The qualifier beside a figure or a title that needs one — a thin sample, a
 * retired count, a fallback build: a 6 px dot in the warning colour, like an idle status, the
 * explanation in its tooltip. It replaced a warning triangle that shouted louder
 * than the number it qualified; keep the tooltip text to the fact itself.
 */
defineProps<{
  text: string
}>()
</script>

<template>
  <UTooltip :text="text" :delay-duration="150">
    <span
      role="img"
      :aria-label="text"
      tabindex="0"
      class="inline-block size-1.5 shrink-0 rounded-full bg-warning outline-none focus-visible:ring-2 focus-visible:ring-warning/40"
    />
  </UTooltip>
</template>
