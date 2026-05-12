<script setup lang="ts">
import type { StaticItemData } from '~/types/static-data'

defineProps<{
  item: StaticItemData
}>()
</script>

<template>
  <UTooltip
    :text="item.name"
    :content="{ side: 'top' }"
    arrow
  >
    <div class="inline-flex items-center">
      <ChampionsChampionAsyncImage
        :src="item.iconUrl"
        :alt="item.name"
        size-class="size-10"
        image-class="rounded-md border border-default bg-default object-cover"
        wrapper-class="rounded-md"
        width="40"
        height="40"
      />
    </div>
  </UTooltip>
</template>
