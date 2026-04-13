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
      <img
        :src="item.iconUrl"
        :alt="item.name"
        class="size-10 rounded-md border border-default bg-default object-cover"
        loading="lazy"
      >
    </div>
  </UTooltip>
</template>
