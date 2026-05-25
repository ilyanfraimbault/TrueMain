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
    <div class="mt-2 flex flex-wrap gap-1">
      <GameTooltipItemIcon
        v-for="(item, index) in items"
        :key="`boots-${item.id}-${index}`"
        :item="item"
        :width="36"
        :height="36"
        class="size-9 rounded"
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
