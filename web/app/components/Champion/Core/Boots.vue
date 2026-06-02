<script setup lang="ts">
import type { BuildItemSet } from '~~/shared/types/champions'
import type { StaticItemData } from '~~/shared/types/static-data'

const props = defineProps<{
  boots: BuildItemSet | null
  itemsMap: Record<number, StaticItemData>
}>()

const items = computed<StaticItemData[]>(() => {
  const ids = props.boots?.itemIds ?? []
  return ids
    .map(id => props.itemsMap[id])
    .filter((item): item is StaticItemData => Boolean(item))
})
</script>

<template>
  <div>
    <h2 class="text-sm font-medium text-muted">
      Boots
    </h2>
    <!-- Fixed from sm: 2 items × 36 px + 1 gap × 4 px = 76 px wide, 36 px tall.
         Boots rarely have more than 1 item, but 2 is the realistic max.
         Height is pinned so the "no data" state occupies the same box.
         Mobile stays fluid (w-full). -->
    <div class="mt-2 flex h-9 w-full shrink-0 items-center gap-1 overflow-hidden sm:w-[76px]">
      <GameTooltipItemIcon
        v-for="(item, index) in items"
        :key="`boots-${item.id}-${index}`"
        :item="item"
        :width="36"
        :height="36"
        class="size-9 shrink-0 rounded"
      />
      <span
        v-if="!items.length"
        class="text-sm text-muted"
      >
        No data
      </span>
    </div>
  </div>
</template>
