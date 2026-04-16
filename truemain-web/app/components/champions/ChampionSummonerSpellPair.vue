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
        <ChampionsChampionAsyncImage
          :src="left.iconUrl"
          :alt="left.name"
          size-class="size-10"
          image-class="rounded-md border border-default bg-default object-cover"
          wrapper-class="rounded-md"
          width="40"
          height="40"
        />
      </UTooltip>

      <UTooltip
        :text="right.name"
        :content="{ side: 'top' }"
        arrow
      >
        <ChampionsChampionAsyncImage
          :src="right.iconUrl"
          :alt="right.name"
          size-class="size-10"
          image-class="rounded-md border border-default bg-default object-cover"
          wrapper-class="rounded-md"
          width="40"
          height="40"
        />
      </UTooltip>
    </template>

  </div>
</template>
