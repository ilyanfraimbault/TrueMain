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
  <SectionCard title="Build path">
    <div class="flex flex-wrap items-center gap-1">
      <template
        v-for="(item, index) in items"
        :key="`bp-${item.id}-${index}`"
      >
        <GameTooltipItemIcon
          :item="item"
          :width="36"
          :height="36"
          class="size-9 rounded"
        />
        <UIcon
          v-if="index < items.length - 1"
          name="i-lucide-chevron-right"
          class="size-4 text-dimmed"
        />
      </template>
      <span
        v-if="!items.length"
        class="text-sm text-muted"
      >
        No data
      </span>
    </div>
  </SectionCard>
</template>
