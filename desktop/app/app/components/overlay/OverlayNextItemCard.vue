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
  <div class="flex items-center gap-2">
    <GameTooltipItemIcon :item="item" :width="28" :height="28" class="size-7 shrink-0 rounded-md ring-1 ring-primary/60" />
    <div class="min-w-0 flex-1">
      <!-- One line for the label and the gold, one for the name: the header is as tall as its icon. -->
      <p class="flex items-center gap-1 text-[10px] font-semibold uppercase tracking-wider text-dimmed">
        <AppMark class="size-2.5" />
        {{ label }}
        <span v-if="missing === 0" class="ml-auto flex items-center gap-0.5 normal-case tracking-normal text-stat-gold">
          <UIcon name="i-lucide-circle-check" class="size-3" />
          Can buy now
        </span>
        <span v-else class="ml-auto normal-case tracking-normal tabular-nums text-muted">
          <span class="text-stat-gold">{{ missing }}</span> to go
        </span>
      </p>
      <p class="truncate text-[13px] font-semibold leading-tight text-highlighted">{{ name }}</p>
    </div>
  </div>
</template>
