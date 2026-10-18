<script setup lang="ts">
import { CURVE_METRICS, type CurveMetric } from '#common/utils/timeline-curves'

// The curves' Gold / XP / CS switch (#2102): the same segmented control as the
// profile's activity window switch — one choice, one inset strip.
const metric = defineModel<CurveMetric>({ required: true })
</script>

<template>
  <fieldset class="flex gap-0.5 rounded-md bg-elevated p-0.5">
    <legend class="sr-only">
      Curve metric
    </legend>
    <button
      v-for="option in CURVE_METRICS"
      :key="option.key"
      type="button"
      class="rounded px-2 py-1 text-xs font-medium transition-colors"
      :class="metric === option.key
        ? 'bg-accented text-highlighted'
        : 'text-dimmed hover:text-default'"
      :aria-pressed="metric === option.key"
      @click="metric = option.key"
    >
      {{ option.label }}
    </button>
  </fieldset>
</template>
