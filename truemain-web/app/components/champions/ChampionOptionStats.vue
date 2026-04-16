<script setup lang="ts">
import { formatPercentage } from '~/utils/items'

const props = defineProps<{
  games: number
  playRate: number
  winRate: number
}>()

function winRateBadgeColor(winRate: number): 'success' | 'error' | 'neutral' {
  if (winRate > 0.55) {
    return 'success'
  }

  if (winRate < 0.45) {
    return 'error'
  }

  return 'neutral'
}

function optionStatsTooltip(games: number, winRate: number): string {
  const wins = Math.round(games * winRate)
  return `${games} games • ${wins} wins`
}
</script>

<template>
  <UTooltip
    :text="optionStatsTooltip(props.games, props.winRate)"
    :content="{ side: 'top' }"
    arrow
  >
    <div class="flex gap-2">
      <UBadge
        color="neutral"
        variant="subtle"
        size="sm"
      >
        {{ formatPercentage(props.playRate) }}
      </UBadge>
      <UBadge
        :color="winRateBadgeColor(props.winRate)"
        variant="soft"
        size="sm"
      >
        {{ formatPercentage(props.winRate) }} WR
      </UBadge>
    </div>
  </UTooltip>
</template>
