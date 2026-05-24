<script setup lang="ts">
import { POSITION_OPTIONS, type ChampionPosition } from '~/utils/positions'

// Segmented icon-only picker for the five Riot positions. Clicking the
// active chip clears the filter — same toggle behaviour as a button group.
const props = defineProps<{
  position: ChampionPosition | null
}>()

const emit = defineEmits<{
  'update:position': [value: ChampionPosition | null]
}>()

function toggle(value: ChampionPosition) {
  if (props.position === value) {
    emit('update:position', null)
  }
  else {
    emit('update:position', value)
  }
}
</script>

<template>
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
      @click="toggle(option.value)"
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
</template>
