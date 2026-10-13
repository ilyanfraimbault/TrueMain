<script setup lang="ts">
import type { Lane, TeamRow } from '~/types/draft'

/**
 * One side of the board: its five picks over their lanes, and the side's damage
 * mix under them. The enemy side's lanes can be corrected by drag; once they
 * have been, a reset puts the guesser's answer back.
 */
defineProps<{
  team: 'ally' | 'enemy'
  rows: TeamRow[]
  selectedCell: number
  opponentCell: number
  /** Portraits to suggest in our own empty slot. */
  suggested?: number[]
  /** Enemy lanes resolved: the cards can be dragged to correct them. */
  correctable?: boolean
  /** The player moved an enemy lane by hand: the reset is offered. */
  hasCorrections?: boolean
  damagePicks: { championId: number, position: string | null, preview?: boolean }[]
}>()

const emit = defineEmits<{ view: [championId: number], swap: [from: Lane, to: Lane], reset: [] }>()
</script>

<template>
  <div class="flex flex-col gap-1.5">
    <div class="relative">
      <DraftTeam
        :team="team"
        :rows="rows"
        :selected-cell="selectedCell"
        :opponent-cell="opponentCell"
        :suggested="suggested"
        :correctable="correctable"
        @view="emit('view', $event)"
        @swap="(from, to) => emit('swap', from, to)"
      />
      <UButton
        v-if="hasCorrections"
        size="xs"
        color="neutral"
        variant="ghost"
        icon="i-lucide-rotate-ccw"
        label="Reset lanes"
        class="absolute -bottom-1.5 right-0"
        @click="emit('reset')"
      />
    </div>
    <DraftDamageBar
      :team="team"
      :picks="damagePicks"
    />
  </div>
</template>
