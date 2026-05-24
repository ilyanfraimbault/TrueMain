<script setup lang="ts">
import type { ChampionStaticListItem } from '~~/shared/types/static-data'

// Searchable champion select with avatar thumbnails. Wraps Nuxt UI's
// USelectMenu so the parent can bind through a numeric championId without
// converting to/from option shapes.
const props = defineProps<{
  champions: ChampionStaticListItem[]
  championId: number | null
  /** Placeholder shown when no champion is selected. */
  placeholder?: string
  /**
   * Tailwind classes applied to the USelectMenu trigger button. Defaults
   * to a compact `w-44` (matches the match-history filter strip). Pass
   * `w-full` (inside a flex-1 wrapper) or a wider fixed value when the
   * picker should grow — the leaderboard filters use it to align text
   * centred via the placeholder data-slot arbitrary variant.
   */
  triggerClass?: string
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
    :placeholder="placeholder ?? 'Any champion'"
    searchable
    searchable-placeholder="Search champion…"
    :class="triggerClass ?? 'w-44'"
    @update:model-value="onChange"
  />
</template>
