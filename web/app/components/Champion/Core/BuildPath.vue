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
  <!-- Reserve room for the worst case (6 items + 5 chevrons) on the
       outer block so the BuildPath footprint stays constant across tabs.
       items-center keeps the actual items centred inside that footprint
       — without it a shorter chain hugs the left edge of the wider block
       and visually drifts off the A2 midpoint between tabs. -->
  <div class="flex min-w-[336px] flex-col items-center">
    <h2 class="text-sm font-medium text-muted">
      Build path
    </h2>
    <div class="mt-2 flex flex-wrap items-center gap-1">
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
  </div>
</template>
