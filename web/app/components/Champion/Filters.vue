<script setup lang="ts">
import type { ChampionPosition } from '~/utils/positions'

// Position + elo + patch pickers for the champion page, laid out as one row.
// The elo-rank select sits between the position picker and the patch select.
// It's optional: the per-player champion page has no rank scoping, so it omits
// the `selectedEloBracket` prop and the select simply isn't rendered.
defineProps<{
  selectedPatch: string
  selectedPosition: ChampionPosition | null
  patchOptions: Array<{ label: string, value: string }>
  selectedEloBracket?: string
}>()

const emit = defineEmits<{
  'update:patch': [value: string]
  'update:position': [value: ChampionPosition | null]
  'update:eloBracket': [value: string]
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
    <ChampionEloFilter
      v-if="selectedEloBracket !== undefined"
      :model-value="selectedEloBracket"
      @update:model-value="value => emit('update:eloBracket', value)"
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
