<script setup lang="ts">
import type { ChampionStaticListItem } from '~~/shared/types/static-data'
import { POSITION_OPTIONS, type ChampionPosition } from '~/utils/positions'

const props = defineProps<{
  champions: ChampionStaticListItem[]
  position: ChampionPosition | null
  championId: number | null
}>()

const emit = defineEmits<{
  'update:position': [value: ChampionPosition | null]
  'update:championId': [value: number | null]
}>()

// USelectMenu wants `{ label, value }` items. Champion ids are stable
// (Riot's `key` field), so we key the option `value` on the id directly
// — that lets the parent bind through a numeric ref without converting.
const championItems = computed(() =>
  [...props.champions]
    .sort((a, b) => a.name.localeCompare(b.name))
    .map(c => ({
      label: c.name,
      value: c.championId,
      avatar: { src: c.iconUrl, alt: c.name },
    })),
)

const selectedChampion = computed(() => championItems.value.find(c => c.value === props.championId))

function togglePosition(value: ChampionPosition) {
  // Clicking the active chip clears the filter — same toggle pattern as a
  // segmented control: tap to set, tap-again to unset.
  if (props.position === value) {
    emit('update:position', null)
  }
  else {
    emit('update:position', value)
  }
}

function onChampionChange(value: { value: number } | undefined) {
  emit('update:championId', value?.value ?? null)
}

function clearAll() {
  emit('update:position', null)
  emit('update:championId', null)
}

const hasAnyFilter = computed(() => props.position !== null || props.championId !== null)
</script>

<template>
  <div class="flex flex-wrap items-center gap-2">
    <div class="flex items-center gap-1 rounded-lg bg-elevated/40 p-1">
      <button
        v-for="option in POSITION_OPTIONS"
        :key="option.value"
        type="button"
        class="inline-flex size-7 items-center justify-center rounded transition-colors"
        :class="position === option.value
          ? 'bg-primary/25 ring-1 ring-primary/50'
          : 'hover:bg-elevated/80'"
        :aria-pressed="position === option.value"
        :title="option.label"
        @click="togglePosition(option.value)"
      >
        <NuxtImg
          :src="option.iconUrl"
          :alt="option.label"
          class="size-4"
          width="16"
          height="16"
        />
      </button>
    </div>

    <USelectMenu
      :model-value="selectedChampion"
      :items="championItems"
      placeholder="Any champion"
      searchable
      searchable-placeholder="Search champion…"
      class="w-44"
      @update:model-value="onChampionChange"
    />

    <UButton
      v-if="hasAnyFilter"
      variant="ghost"
      color="neutral"
      size="xs"
      icon="i-lucide-x"
      @click="clearAll"
    >
      Clear
    </UButton>
  </div>
</template>
