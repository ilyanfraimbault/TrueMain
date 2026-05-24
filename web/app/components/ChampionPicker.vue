<script setup lang="ts">
import type { ChampionStaticListItem } from '~~/shared/types/static-data'

// Searchable champion select with avatar thumbnails. Wraps Nuxt UI's
// USelectMenu so the parent can bind through a numeric championId without
// converting to/from option shapes.
const props = defineProps<{
  champions: ChampionStaticListItem[]
  championId: number | null
  /**
   * Tailwind class controlling the select trigger's width. Defaults to a
   * fixed `w-44` (compact, matches the match-history filter strip); pass
   * `w-full` (with a flex-1 wrapper) when the picker should stretch to
   * fill the remaining horizontal space — the leaderboard filters use it
   * so the search field grows with the viewport.
   */
  width?: string
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
    :class="props.width ?? 'w-44'"
    @update:model-value="onChange"
  />
</template>
