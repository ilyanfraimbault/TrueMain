<script setup lang="ts">
import type { RankedQueue } from '~/types/record'
import { formatPercentage } from '#shared/utils/ddragon'
import { formatLeaguePoints, formatRank, tierColor } from '#common/utils/tiers'

// The player's Ranked Flex standing, one line under the ranked card's Solo/Duo
// block: crest, rank and LP, record. Placements read as such; a player who has
// not played Flex this season sees nothing.
const props = defineProps<{ queue: RankedQueue | null }>()

const ranked = computed(() => Boolean(props.queue?.tier) && props.queue?.tier !== 'NONE')
const played = computed(() => (props.queue ? props.queue.wins + props.queue.losses : 0))
const record = computed(() => {
  const queue = props.queue
  if (!queue || played.value === 0) return null
  return `${queue.wins}W ${queue.losses}L · ${formatPercentage(queue.wins / played.value, 0)}`
})
</script>

<template>
  <div
    v-if="queue && (ranked || played > 0)"
    class="flex flex-wrap items-center gap-x-2 gap-y-0.5 border-t border-default pt-3 text-xs"
    data-testid="flex-standing"
  >
    <!-- Each group stays whole; a long apex rank pushes the record to a line of its own. -->
    <span class="flex items-center gap-2 whitespace-nowrap">
      <RankIcon v-if="ranked" :tier="queue.tier" :size="20" />
      <span class="font-semibold uppercase tracking-wide text-muted">Flex</span>
      <span v-if="ranked" class="font-semibold tabular-nums" :class="tierColor(queue.tier)">
        {{ formatRank(queue.tier, queue.division) }}
        <span class="text-default">· {{ formatLeaguePoints(queue.leaguePoints) }}</span>
      </span>
      <span v-else class="text-muted">Placements · {{ played }} played</span>
    </span>
    <span v-if="ranked && record" class="ml-auto whitespace-nowrap text-muted tabular-nums">{{ record }}</span>
  </div>
</template>
