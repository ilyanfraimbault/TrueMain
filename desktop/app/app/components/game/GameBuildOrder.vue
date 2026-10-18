<script setup lang="ts">
import type { StaticItemData } from '#shared/types/static-data'
import type { NextItemOrderStep } from '~/utils/next-item'

/**
 * The build as one row of small icons (#2101): where the game stands in the
 * mains' order — completed items dimmed, the next one ringed, the rest still
 * to come, the boots at the step the mains complete them at. Under the next
 * item in the game page's panel and on the overlay.
 */
withDefaults(defineProps<{ steps: NextItemOrderStep[], statics: Record<number, StaticItemData>, size?: number }>(), { size: 24 })
</script>

<template>
  <ol v-if="steps.length" class="flex items-center gap-1" aria-label="Build order">
    <li v-for="(step, index) in steps" :key="`${step.itemId}-${index}`" :aria-current="step.state === 'Next' ? 'step' : undefined">
      <GameTooltipItemIcon
        :item="statics[step.itemId] ?? null"
        :width="size"
        :height="size"
        class="rounded"
        :class="{
          'opacity-35 grayscale': step.state === 'Completed',
          'ring-2 ring-primary': step.state === 'Next',
          'opacity-80': step.state === 'Upcoming',
        }"
        :style="{ width: `${size}px`, height: `${size}px` }"
      />
    </li>
  </ol>
</template>
