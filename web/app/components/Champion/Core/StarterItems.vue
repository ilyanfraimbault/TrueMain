<script setup lang="ts">
import type { BuildItemSet } from '~~/shared/types/champions'
import type { StaticItemData } from '~~/shared/types/static-data'

const props = defineProps<{
  starter: BuildItemSet | null
  itemsMap: Record<number, StaticItemData>
}>()

const items = computed<StaticItemData[]>(() => {
  const ids = props.starter?.itemIds ?? []
  return ids
    .map(id => props.itemsMap[id])
    .filter((item): item is StaticItemData => Boolean(item))
})
</script>

<template>
  <div>
    <h2 class="text-sm font-medium text-muted">
      Starter
    </h2>
    <!-- Fixed: 3 items × 36 px + 2 gaps × 4 px = 116 px wide, 36 px tall.
         Width is capped at the 3-item worst case; height is pinned so the
         "no data" state occupies the same box without collapsing the row. -->
    <div class="mt-2 flex h-9 w-[116px] shrink-0 items-center gap-1 overflow-hidden">
      <GameTooltipItemIcon
        v-for="(item, index) in items"
        :key="`starter-${item.id}-${index}`"
        :item="item"
        :width="36"
        :height="36"
        class="size-9 shrink-0 rounded"
      />
    </div>
  </div>
</template>
