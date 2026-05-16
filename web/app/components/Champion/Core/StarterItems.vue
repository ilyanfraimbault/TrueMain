<script setup lang="ts">
import type { ItemSetOptionResponse } from '~~/shared/types/champions'
import type { ChampionStaticData, StaticItemData } from '~~/shared/types/static-data'

const props = defineProps<{
  starter: ItemSetOptionResponse | null
  championStatic: ChampionStaticData
}>()

const items = computed<StaticItemData[]>(() => {
  const ids = props.starter?.itemIds ?? []
  return ids
    .map(id => props.championStatic.items[id])
    .filter((item): item is StaticItemData => Boolean(item))
})
</script>

<template>
  <div>
    <h2 class="text-sm font-medium text-muted">
      Starter
    </h2>
    <div class="mt-2 flex flex-wrap gap-1">
      <NuxtImg
        v-for="(item, index) in items"
        :key="`starter-${item.id}-${index}`"
        :src="item.iconUrl"
        :alt="item.name"
        :title="item.name"
        width="36"
        height="36"
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
