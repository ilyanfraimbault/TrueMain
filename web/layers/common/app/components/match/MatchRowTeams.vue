<script setup lang="ts">
import type { MatchSummaryParticipant } from '#shared/types/matches'
import type { ChampionStaticListItem } from '#shared/types/static-data'
import { riotIdLabel } from '#shared/utils/truemain-path'
import { compareByPosition, POSITION_BY_VALUE } from '#common/utils/positions'

// Both compositions of a match row, the player's team over the enemy's, each in
// lane order so a column pairs laner against laner.
const props = defineProps<{
  participants: MatchSummaryParticipant[]
  selfTeamId: number
  championById: Map<number, ChampionStaticListItem>
}>()

const teams = computed(() => [
  props.participants.filter(p => p.teamId === props.selfTeamId).sort(compareByPosition),
  props.participants.filter(p => p.teamId !== props.selfTeamId).sort(compareByPosition),
])

function participantLabel(p: MatchSummaryParticipant): string {
  const champion = props.championById.get(p.championId)?.name ?? `Champion ${p.championId}`
  const role = p.position ? POSITION_BY_VALUE.get(p.position)?.label : null
  const who = p.gameName ? ` — ${riotIdLabel(p.gameName, p.tagLine)}` : ''
  return role ? `${champion} · ${role}${who}` : `${champion}${who}`
}
</script>

<template>
  <div class="flex flex-col gap-0.5">
    <div v-for="(team, side) in teams" :key="side" class="flex gap-0.5">
      <SkeletonImage
        v-for="(p, index) in team"
        :key="index"
        :src="championById.get(p.championId)?.iconUrl ?? null"
        :alt="participantLabel(p)"
        :title="participantLabel(p)"
        :width="16"
        :height="16"
        loading="lazy"
        class="size-4 rounded-sm"
      />
    </div>
  </div>
</template>
