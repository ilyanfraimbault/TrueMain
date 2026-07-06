<script setup lang="ts">
import type { ChampionPosition } from '~/utils/positions'
import { ELO_BRACKET_ALL, ELO_BRACKET_OPTIONS, isEloBracket, type EloBracket } from '~/utils/elo-brackets'

// `hideElo` drops the elo picker for single-player views (a truemain's own
// champion page), where every game is essentially one rank so a rank filter
// is meaningless. `selectedEloBracket` then defaults to ALL and is unused.
withDefaults(defineProps<{
  selectedPatch: string
  selectedPosition: ChampionPosition | null
  patchOptions: Array<{ label: string, value: string }>
  selectedEloBracket?: EloBracket
  hideElo?: boolean
}>(), {
  selectedEloBracket: ELO_BRACKET_ALL,
  hideElo: false,
})

const emit = defineEmits<{
  'update:patch': [value: string]
  'update:position': [value: ChampionPosition | null]
  'update:eloBracket': [value: EloBracket]
}>()

const eloBracketOptions = ELO_BRACKET_OPTIONS

function onPatchChange(value: unknown) {
  if (typeof value !== 'string' || !value) return
  emit('update:patch', value)
}

function onEloBracketChange(value: unknown) {
  if (!isEloBracket(value)) return
  emit('update:eloBracket', value)
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
      v-if="!hideElo"
      :model-value="selectedEloBracket"
      :items="eloBracketOptions"
      placeholder="Elo"
      class="w-36"
      aria-label="Elo bracket"
      @update:model-value="onEloBracketChange"
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
