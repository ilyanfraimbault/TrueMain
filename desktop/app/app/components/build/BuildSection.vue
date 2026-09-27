<script setup lang="ts">
/**
 * A titled block of a build view: the title, and the win rate and games of the
 * choice under it. `sub` is the smaller heading inside a block ("Starting items"
 * under "Items").
 */
withDefaults(defineProps<{
  title: string
  /** Win rate of this block's own choice, 0..1. */
  winRate?: number | null
  games?: number | null
  sub?: boolean
}>(), { winRate: null, games: null, sub: false })
</script>

<template>
  <section class="flex flex-col gap-2">
    <div class="flex items-baseline gap-2">
      <h3 :class="sub ? 'text-xs font-medium text-muted' : 'text-sm font-semibold text-highlighted'">{{ title }}</h3>
      <span v-if="games" class="text-[11px] tabular-nums text-dimmed">{{ games.toLocaleString('en-US') }} games</span>
      <span v-if="winRate !== null" class="ml-auto text-[11px] font-semibold tabular-nums" :class="winRateTone(winRate)">{{ (winRate * 100).toFixed(1) }}%</span>
      <slot name="trailing" />
    </div>
    <slot />
  </section>
</template>
