<script setup lang="ts">
import type { MatchDetailParticipant } from '#shared/types/match-detail'
import type { MatchDetailStat } from './MatchDetailStatGrid.vue'

// The selected player's lane at 15 minutes against the opposing position.
const props = defineProps<{
  participant: MatchDetailParticipant
}>()

// A laning differential is a measurement, so it speaks the data axis rather
// than the blue/red the row uses for *which side won*. Ahead is the accent,
// behind steps down to the neutral end — the same one-sided read as every other
// number on the site since #1096.
function diffTone(value: number) {
  if (value > 0) return 'text-data-good'
  if (value < 0) return 'text-data-bad'
  return 'text-muted'
}
function diff(value: number, label: string): MatchDetailStat {
  return { value: value > 0 ? `+${value}` : `${value}`, label, tone: diffTone(value) }
}

const stats = computed<MatchDetailStat[] | null>(() => {
  const laning = props.participant.laning15
  if (!laning) return null
  // Winning the level-2 race is an advantage, not a victory: it reads on the
  // data axis with the three diffs beside it, not in the result colours.
  const levelTwo = props.participant.firstToLevelTwo
  return [
    diff(laning.csDiff, 'cs diff'),
    diff(laning.goldDiff, 'gold diff'),
    diff(laning.xpDiff, 'xp diff'),
    {
      value: levelTwo === null ? '—' : levelTwo ? 'Yes' : 'No',
      label: 'first lvl 2',
      tone: levelTwo ? 'text-data-good' : 'text-muted',
    },
  ]
})
</script>

<template>
  <MatchDetailCard title="Laning phase (at 15)" icon="i-lucide-swords">
    <MatchDetailStatGrid v-if="stats" :stats="stats" />
    <p v-else class="text-[11px] text-muted">No laning data for this game.</p>
  </MatchDetailCard>
</template>
