<script setup lang="ts">
import type { StaticItemData } from '#shared/types/static-data'
import type { PathTile } from '~/utils/build-path'

/**
 * One component of the build path (#2183) and what it costs from here:
 * owned (dimmed and checked), bought now (ringed in the accent, its price
 * too — "≈" when the gold's 50-gold step straddles it) or later (muted).
 */
const props = withDefaults(defineProps<{ tile: PathTile, statics: Record<number, StaticItemData>, size?: number }>(), { size: 24 })

const label = computed(() => {
  const name = props.statics[props.tile.itemId]?.name ?? `Item ${props.tile.itemId}`
  switch (props.tile.state) {
    case 'owned': return `${name}: held`
    case 'buy': return props.tile.approx ? `${name}: about enough gold to buy now` : `${name}: buy now`
    default: return `${name}: later`
  }
})
</script>

<template>
  <div class="flex flex-col items-center gap-0.5" :aria-label="label">
    <div class="relative">
      <GameTooltipItemIcon
        :item="statics[tile.itemId] ?? null"
        :width="size"
        :height="size"
        class="rounded"
        :class="{
          'opacity-40 grayscale': tile.state === 'owned',
          'ring-2 ring-primary': tile.state === 'buy',
          'opacity-55': tile.state === 'later',
        }"
        :style="{ width: `${size}px`, height: `${size}px` }"
      />
      <span v-if="tile.state === 'owned'" class="absolute -right-1 -bottom-1 flex size-3 items-center justify-center rounded-full bg-default ring-1 ring-primary/50">
        <UIcon name="i-lucide-check" class="size-2 text-primary" />
      </span>
    </div>
    <span
      class="text-[10px] leading-none tabular-nums"
      :class="tile.state === 'buy' ? 'font-semibold text-primary' : 'text-dimmed'"
    >{{ tile.state === 'owned' ? '\u00A0' : `${tile.approx ? '≈' : ''}${tile.cost}` }}</span>
  </div>
</template>
