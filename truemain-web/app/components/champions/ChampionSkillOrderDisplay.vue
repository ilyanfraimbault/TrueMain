<script setup lang="ts">
import type { StaticChampionSpellData } from '~/types/static-data'

defineProps<{
  spells: StaticChampionSpellData[]
}>()
</script>

<template>
  <div class="flex flex-wrap items-center gap-1">
    <template
      v-for="spell in spells"
      :key="`${spell.key}-${spell.name}`"
    >
      <UTooltip
        :text="spell.name"
        :content="{ side: 'top' }"
        arrow
      >
        <div class="relative">
          <img
            :src="spell.iconUrl"
            :alt="spell.name"
            class="size-11 rounded-md border border-default bg-default object-cover"
            loading="lazy"
          >
          <UBadge
            color="neutral"
            variant="subtle"
            size="xs"
            class="absolute -bottom-2 left-1/2 -translate-x-1/2 border border-default"
          >
            {{ spell.key }}
          </UBadge>
        </div>
      </UTooltip>
    </template>

  </div>
</template>
