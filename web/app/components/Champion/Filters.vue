<script setup lang="ts">
import type { ChampionPosition } from '~/utils/positions'

// Position + patch pickers for the champion page. The elo-rank filter lives
// in its own emblem strip (ChampionEloFilter) so it isn't offered here.
defineProps<{
  selectedPatch: string
  selectedPosition: ChampionPosition | null
  patchOptions: Array<{ label: string, value: string }>
}>()

const emit = defineEmits<{
  'update:patch': [value: string]
  'update:position': [value: ChampionPosition | null]
}>()

function onPatchChange(value: unknown) {
  if (typeof value !== 'string' || !value) return
  emit('update:patch', value)
}
</script>

<template>
  <div class="flex flex-wrap items-center gap-2">
    <RolePicker
      :position="selectedPosition"
      hide-all
      @update:position="value => emit('update:position', value)"
    />
    <USelect
      :model-value="selectedPatch"
      :items="patchOptions"
      placeholder="Patch"
      class="w-28"
      @update:model-value="onPatchChange"
    />
  </div>
</template>
