<script setup lang="ts">
import type { ChampionStaticListItem } from '~~/shared/types/static-data'

// Searchable champion select with avatar thumbnails. Wraps Nuxt UI's
// USelectMenu so the parent can bind through a numeric championId without
// converting to/from option shapes.
const props = defineProps<{
  champions: ChampionStaticListItem[]
  championId: number | null
}>()

const emit = defineEmits<{
  'update:championId': [value: number | null]
}>()

const championItems = computed(() =>
  [...props.champions]
    .sort((a, b) => a.name.localeCompare(b.name))
    .map(c => ({
      label: c.name,
      value: c.championId,
      avatar: { src: c.iconUrl, alt: c.name },
    })),
)

const selectedChampion = computed(() =>
  championItems.value.find(c => c.value === props.championId))

function onChange(value: { value: number } | undefined) {
  emit('update:championId', value?.value ?? null)
}
</script>

<template>
  <USelectMenu
    :model-value="selectedChampion"
    :items="championItems"
    placeholder="Any champion"
    searchable
    searchable-placeholder="Search champion…"
    class="w-44"
    @update:model-value="onChange"
  />
</template>
