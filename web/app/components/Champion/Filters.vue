<script setup lang="ts">
import { isChampionPosition, type ChampionPosition } from '~/utils/positions'

const props = defineProps<{
  selectedPatch: string
  selectedPosition: ChampionPosition | ''
  patchOptions: Array<{ label: string, value: string }>
  positionOptions: Array<{ label: string, value: ChampionPosition, iconUrl: string }>
}>()

const emit = defineEmits<{
  'update:patch': [value: string]
  'update:position': [value: ChampionPosition]
}>()

function onPatchChange(value: unknown) {
  if (typeof value !== 'string' || !value) return
  emit('update:patch', value)
}

function onPositionChange(value: unknown) {
  if (!isChampionPosition(value)) return
  emit('update:position', value)
}
</script>

<template>
  <div class="flex flex-wrap items-center gap-2">
    <USelect
      :model-value="selectedPatch"
      :items="patchOptions"
      placeholder="Patch"
      class="w-28"
      @update:model-value="onPatchChange"
    />
    <USelect
      :model-value="selectedPosition || undefined"
      :items="positionOptions"
      placeholder="Position"
      class="w-32"
      @update:model-value="onPositionChange"
    />
  </div>
</template>
