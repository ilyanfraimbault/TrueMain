<script setup lang="ts">
import type { BuildItemPath } from '~~/shared/types/champions'
import type { StaticItemData } from '~~/shared/types/static-data'

const props = defineProps<{
  path: BuildItemPath | null
  itemsMap: Record<number, StaticItemData>
}>()

const items = computed<StaticItemData[]>(() => {
  const ids = props.path?.itemIds ?? []
  return ids
    .map(id => props.itemsMap[id])
    .filter((item): item is StaticItemData => Boolean(item))
})
</script>

<template>
  <!-- Fixed from sm: 6 items × 36 px + 5 chevrons × 16 px + 10 gaps × 4 px = 336 px
       wide, 36 px tall. Width locks at the 6-item worst case; height is pinned
       so no-data state doesn't collapse the row. On mobile (< sm) the fixed
       width is removed and items can wrap naturally inside available width.
       justify-center keeps a short chain centred in its parent's A2 area. -->
  <div class="flex flex-col items-center">
    <h2 class="text-sm font-medium text-muted">
      Build path
    </h2>
    <div class="mt-2 flex h-9 items-center justify-center gap-1 overflow-hidden sm:w-[336px]">
      <template
        v-for="(item, index) in items"
        :key="`bp-${item.id}-${index}`"
      >
        <GameTooltipItemIcon
          :item="item"
          :width="36"
          :height="36"
          class="size-9 shrink-0 rounded"
        />
        <UIcon
          v-if="index < items.length - 1"
          name="i-lucide-chevron-right"
          class="size-4 shrink-0 text-dimmed"
        />
      </template>
    </div>
  </div>
</template>
