<script setup lang="ts">
import type { BuildItemPath } from '~~/shared/types/champions'
import type { ChampionStaticData, StaticItemData } from '~~/shared/types/static-data'

const props = defineProps<{
  path: BuildItemPath | null
  championStatic: ChampionStaticData
}>()

const items = computed<StaticItemData[]>(() => {
  const ids = props.path?.itemIds ?? []
  return ids
    .map(id => props.championStatic.items[id])
    .filter((item): item is StaticItemData => Boolean(item))
})
</script>

<template>
  <div>
    <h2 class="text-sm font-medium text-muted">
      Build path
    </h2>
    <!-- Reserve room for the worst case (6 items + 5 chevrons) so the
         BuildPath block keeps the same footprint across tabs. With
         A2b's justify-center, a shorter chain would otherwise centre on
         the row midpoint with a narrower block and shift the items'
         absolute x position between tabs. -->
    <div class="mt-2 flex min-w-[336px] flex-wrap items-center gap-1">
      <template
        v-for="(item, index) in items"
        :key="`bp-${item.id}-${index}`"
      >
        <NuxtImg
          :src="item.iconUrl"
          :alt="item.name"
          :title="item.name"
          width="36"
          height="36"
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
  </div>
</template>
