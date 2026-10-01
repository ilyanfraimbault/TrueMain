<script setup lang="ts">
import type { MetricReading } from '~/utils/player-form'
import { RECENT_GAMES } from '~/utils/player-form'

/**
 * One metric of the player's recent form: the value over the last games, how
 * far it sits from their own average — in the ranked card's LP-delta idiom, a
 * trend arrow on the data axis — and the metric game by game underneath. A move
 * too small to matter, or a sample too short to split, shows no delta at all.
 */
const props = defineProps<{
  reading: MetricReading
  /** Counted games the average is taken over. */
  sample: number
}>()

const tone = computed(() => {
  if (props.reading.improved === null) return 'flat'
  return props.reading.improved ? 'good' : 'bad'
})

const delta = computed(() => {
  const { delta, metric } = props.reading
  if (delta === null || tone.value === 'flat') return null
  return {
    text: metric.format(Math.abs(delta)),
    icon: delta > 0 ? 'i-lucide-trending-up' : 'i-lucide-trending-down',
    class: tone.value === 'good' ? 'text-data-good' : 'text-data-bad',
  }
})
</script>

<template>
  <div class="flex min-w-0 flex-col gap-1.5 rounded-lg bg-ink-950/55 px-3 pb-2 pt-2.5 ring-1 ring-white/5 backdrop-blur-md">
    <span class="stat-label truncate">{{ reading.metric.label }}</span>
    <div class="flex items-baseline justify-between gap-2">
      <span class="stat-value text-xl leading-none">
        {{ reading.value === null ? '—' : reading.metric.format(reading.value) }}
      </span>
      <UTooltip v-if="delta" :text="`Last ${RECENT_GAMES} games against your average over ${sample}`" :delay-duration="150">
        <span class="inline-flex items-center gap-1 text-xs font-semibold tabular-nums text-default">
          <UIcon :name="delta.icon" class="size-3.5" :class="delta.class" />
          {{ delta.text }}
        </span>
      </UTooltip>
    </div>
    <DashboardSparkline :values="reading.series" :tone="tone" />
  </div>
</template>
