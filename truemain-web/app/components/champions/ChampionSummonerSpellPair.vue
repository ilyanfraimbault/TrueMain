<script setup lang="ts">
import type { StaticSummonerSpellData } from '~/types/static-data'

defineProps<{
  left: StaticSummonerSpellData | null
  right: StaticSummonerSpellData | null
}>()
</script>

<template>
  <div class="flex items-center gap-1">
    <template v-if="left && right">
      <UTooltip
        :text="left.name"
        :content="{ side: 'top' }"
        arrow
      >
        <img
          :src="left.iconUrl"
          :alt="left.name"
          class="size-10 rounded-md border border-default bg-default object-cover"
          loading="lazy"
        >
      </UTooltip>

      <UTooltip
        :text="right.name"
        :content="{ side: 'top' }"
        arrow
      >
        <img
          :src="right.iconUrl"
          :alt="right.name"
          class="size-10 rounded-md border border-default bg-default object-cover"
          loading="lazy"
        >
      </UTooltip>
    </template>

  </div>
</template>
