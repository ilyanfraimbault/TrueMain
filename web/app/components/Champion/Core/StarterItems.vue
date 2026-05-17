<script setup lang="ts">
import type { BuildItemSet } from '~~/shared/types/champions'
import type { ChampionStaticData, StaticItemData } from '~~/shared/types/static-data'

const props = defineProps<{
  starter: BuildItemSet | null
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
    <!-- Reserve room for the worst case (3 starter items + 2 gaps) so the
         A1 column width stays constant when a tab only carries 2 items.
         Otherwise A1 shrinks, A2 widens, and the rest of the row shifts. -->
    <div class="mt-2 flex min-w-[116px] flex-wrap gap-1">
      <SkeletonImage
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
