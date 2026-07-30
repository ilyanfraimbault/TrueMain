<script setup lang="ts">
import type { ChampionPosition } from '~/utils/positions'
import type { ChampionStaticListItem } from '~~/shared/types/static-data'

// Position + elo + patch + matchup pickers for the champion page, laid out as
// one row. The elo-rank select sits between the position picker and the patch
// select. It's optional: the per-player champion page has no rank scoping, so it
// omits the `selectedEloBracket` prop and the select simply isn't rendered — and
// the same goes for the matchup picker, which needs a champion list to search.
defineProps<{
  selectedPatch: string
  selectedPosition: ChampionPosition | null
  patchOptions: Array<{ label: string, value: string }>
  selectedEloBracket?: string
  /**
   * Opponents offered by the matchup picker (#923). Omit to hide it. The page
   * excludes the champion itself — a champion has no matchup against itself.
   */
  opponentOptions?: ChampionStaticListItem[]
  selectedOpponentId?: number | null
}>()

const emit = defineEmits<{
  'update:patch': [value: string]
  'update:position': [value: ChampionPosition | null]
  'update:eloBracket': [value: string]
  'update:opponentChampionId': [value: number | null]
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
    <!-- The matchup sits with the other filters because it scopes the same
         sections they do: pick an opponent and every build below is recomputed
         from the games where the two actually met. -->
    <div v-if="opponentOptions" class="flex items-center gap-1.5">
      <span class="text-xs text-dimmed">vs</span>
      <!-- The picker owns its own clear affordance; a second one beside it would
           be two crosses for one action. -->
      <ChampionPicker
        :champions="opponentOptions"
        :champion-id="selectedOpponentId ?? null"
        placeholder="Any opponent"
        trigger-class="w-44"
        @update:champion-id="value => emit('update:opponentChampionId', value)"
      />
    </div>
  </div>
</template>
