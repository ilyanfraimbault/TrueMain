<!--
  The dashboard's roles: the site's role distribution (`ProfilePositionBreakdown`)
  over the client's latest games, each role's win rate beside its game count.
-->
<script setup lang="ts">
import type { ProfilePositionStat } from '#shared/types/profile'
import { formatPercentage } from '#shared/utils/ddragon'
import type { LaneLine } from '~/utils/player-form'
import { winRateTone } from '#common/utils/rate-tone'

const props = defineProps<{ lanes: LaneLine[] }>()

const winsByPosition = computed(() => new Map(props.lanes.map(lane => [lane.position, lane.wins])))

const positions = computed<ProfilePositionStat[]>(() => {
  const total = props.lanes.reduce((sum, lane) => sum + lane.games, 0)
  return props.lanes.map(lane => ({ position: lane.position, games: lane.games, rate: total ? lane.games / total : 0 }))
})

const winRate = (role: ProfilePositionStat) => (winsByPosition.value.get(role.position) ?? 0) / role.games
</script>

<template>
  <ProfilePositionBreakdown :positions="positions" title="Roles">
    <template #games="{ role }">
      {{ role.games }} game{{ role.games === 1 ? '' : 's' }} ·
      <span
        class="font-semibold"
        :class="winRateTone(winRate(role))"
        :title="`${winsByPosition.get(role.position) ?? 0}W ${role.games - (winsByPosition.get(role.position) ?? 0)}L`"
      >{{ formatPercentage(winRate(role), 0) }} WR</span>
    </template>
  </ProfilePositionBreakdown>
</template>
