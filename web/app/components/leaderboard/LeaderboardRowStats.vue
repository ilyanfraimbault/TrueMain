<script setup lang="ts">
import type { LeaderboardStats } from '~~/shared/types/leaderboard'
import { leaderboardStatLabels } from '#common/utils/leaderboard-row'
import { winRateTone } from '#common/utils/rate-tone'

// Games / KDA / WR at the far end of a `LeaderboardRow`. The fixed widths are
// load-bearing for the row's @container tiers. All three cells are always
// rendered, an em dash standing in for a figure the aggregate can't supply:
// dropping a cell handed its width back to the row's flex spacers, nudging
// every column between them off the grid the rest of the list sits on.
// Only the win rate takes a colour: games is a count and KDA has no agreed
// neutral point, so neither has a better/worse reading to encode. The host
// sets the display.
const props = defineProps<{ stats: LeaderboardStats }>()

const labels = computed(() => leaderboardStatLabels(props.stats))
</script>

<template>
  <div class="shrink-0 items-center gap-4">
    <div class="flex w-12 justify-end">
      <StatBlock :value="labels.games" label="games" size="sm" align="end" />
    </div>
    <div class="flex w-12 justify-end">
      <StatBlock :value="labels.kda" label="KDA" size="sm" align="end" />
    </div>
    <div class="flex w-12 justify-end">
      <StatBlock
        :value="labels.winRate"
        label="WR"
        size="sm"
        align="end"
        :value-class="winRateTone(stats.winRate)"
      />
    </div>
  </div>
</template>
