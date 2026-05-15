<script setup lang="ts">
import type { ChampionCoreResponse, ItemSetOptionResponse } from '~~/shared/types/champions'
import type { ChampionStaticData, StaticItemData } from '~~/shared/types/static-data'

const props = defineProps<{
  core: ChampionCoreResponse | null
  championStatic: ChampionStaticData
}>()

function itemsFromIds(ids: number[] | undefined | null): StaticItemData[] {
  if (!ids) return []
  return ids
    .map(id => props.championStatic.items[id])
    .filter((item): item is StaticItemData => Boolean(item))
}

function itemsFromSet(set: ItemSetOptionResponse | null): StaticItemData[] {
  return itemsFromIds(set?.itemIds ?? null)
}

const starterItems = computed(() => itemsFromSet(props.core?.starterItems ?? null))
const bootsItems = computed(() => itemsFromSet(props.core?.boots ?? null))
const buildPathItems = computed(() => itemsFromIds(props.core?.buildPath?.itemIds ?? null))
</script>

<template>
  <div class="grid gap-4 sm:grid-cols-3">
    <div>
      <h3 class="text-sm font-medium text-muted">
        Starter items
      </h3>
      <div class="mt-2 flex flex-wrap gap-1">
        <NuxtImg
          v-for="(item, index) in starterItems"
          :key="`starter-${item.id}-${index}`"
          :src="item.iconUrl"
          :alt="item.name"
          :title="item.name"
          width="36"
          height="36"
          class="size-9 rounded"
        />
        <span
          v-if="!starterItems.length"
          class="text-sm text-muted"
        >
          No data
        </span>
      </div>
    </div>

    <div>
      <h3 class="text-sm font-medium text-muted">
        Boots
      </h3>
      <div class="mt-2 flex flex-wrap gap-1">
        <NuxtImg
          v-for="(item, index) in bootsItems"
          :key="`boots-${item.id}-${index}`"
          :src="item.iconUrl"
          :alt="item.name"
          :title="item.name"
          width="36"
          height="36"
          class="size-9 rounded"
        />
        <span
          v-if="!bootsItems.length"
          class="text-sm text-muted"
        >
          No data
        </span>
      </div>
    </div>

    <div>
      <h3 class="text-sm font-medium text-muted">
        Dominant build path
      </h3>
      <div class="mt-2 flex flex-wrap gap-1">
        <NuxtImg
          v-for="(item, index) in buildPathItems"
          :key="`bp-${item.id}-${index}`"
          :src="item.iconUrl"
          :alt="item.name"
          :title="item.name"
          width="36"
          height="36"
          class="size-9 rounded"
        />
        <span
          v-if="!buildPathItems.length"
          class="text-sm text-muted"
        >
          No data
        </span>
      </div>
    </div>
  </div>
</template>
