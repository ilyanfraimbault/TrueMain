<script setup lang="ts">
import type { MatchDetailParticipant } from '#shared/types/match-detail'
import type { MatchDetailStat } from './MatchDetailStatGrid.vue'

// The selected player's whole-game rates. Ward counts and control wards are
// absent on purpose: the detail payload doesn't carry them yet.
const props = defineProps<{
  participant: MatchDetailParticipant
}>()

const stats = computed<MatchDetailStat[]>(() => {
  const p = props.participant
  return [
    { value: p.csPerMin.toFixed(1), label: 'CS/m', tone: 'text-default' },
    { value: p.visionPerMin.toFixed(1), label: 'VS/m', tone: 'text-default' },
    { value: p.damagePerMin.toFixed(0), label: 'DMG/m', tone: 'text-default' },
    { value: p.goldPerMin.toFixed(0), label: 'Gold/m', tone: 'text-default' },
  ]
})
</script>

<template>
  <MatchDetailCard title="Global stats" icon="i-lucide-activity">
    <MatchDetailStatGrid :stats="stats" />
  </MatchDetailCard>
</template>
