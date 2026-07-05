<script setup lang="ts">
import type { ChampionPosition } from '~/utils/positions'
import { ELO_BRACKET_OPTIONS, isEloBracket, type EloBracket } from '~/utils/elo-brackets'

// `selectedEloBracket` is optional: the global champion page passes it to expose
// the elo-bracket picker (issue #526), while the player-scoped page omits it — a
// single player sits in one bracket, so filtering their own games by elo is not
// meaningful (and the player-scoped endpoint doesn't accept it). When it's
// undefined the elo selector is hidden entirely.
defineProps<{
  selectedPatch: string
  selectedPosition: ChampionPosition | null
  selectedEloBracket?: EloBracket
  patchOptions: Array<{ label: string, value: string }>
}>()

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
      v-if="selectedEloBracket"
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
