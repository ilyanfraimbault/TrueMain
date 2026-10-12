<script setup lang="ts">
import type { StaticItemData } from '#shared/types/static-data'

/**
 * The overlay's next item once it is known — or the starter, until one is
 * bought: the item, its name and the gold still to earn. Drawn by `OverlayNextItem` over a game and by the overlay's
 * preview with a sample, so a panel is placed at the size it will have.
 */
withDefaults(defineProps<{ item: StaticItemData | null, name: string, missing: number, label?: string }>(), { label: 'Next item' })
</script>

<template>
  <div class="flex items-center gap-2.5">
    <GameTooltipItemIcon :item="item" :width="36" :height="36" class="size-9 shrink-0 rounded-md ring-1 ring-primary/60" />
    <div class="min-w-0 flex-1">
      <p class="flex items-center gap-1 text-[10px] font-semibold uppercase tracking-wider text-dimmed">
        <AppMark class="size-2.5" />
        {{ label }}
      </p>
      <p class="truncate text-[13px] font-semibold leading-tight text-highlighted">{{ name }}</p>
      <p v-if="missing === 0" class="flex items-center gap-1 text-[11px] font-medium text-stat-gold">
        <UIcon name="i-lucide-circle-check" class="size-3" />
        Can buy now
      </p>
      <p v-else class="text-[11px] tabular-nums text-muted">
        <span class="text-stat-gold">{{ missing }}</span> gold to go
      </p>
    </div>
  </div>
</template>
