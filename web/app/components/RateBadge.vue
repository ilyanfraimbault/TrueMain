<script setup lang="ts">
import { formatPercentage } from '~~/shared/utils/ddragon'

// `games` is what the percentages are computed from. It is shown whenever the
// caller passes it (#923): a matchup slice is usually small — measured on
// production, the median champion-vs-opponent pair holds 4 games on a patch — so
// "100% win" next to it must be readable as "1 of 1", not as a trend.
defineProps<{
  pickRate: number
  winRate: number
  games?: number
}>()
</script>

<template>
  <div class="flex shrink-0 items-center gap-2 whitespace-nowrap">
    <span v-if="games !== undefined" class="text-xs text-dimmed tabular-nums">
      {{ games }} {{ games === 1 ? 'game' : 'games' }}
    </span>
    <UBadge
      color="neutral"
      size="sm"
    >
      {{ formatPercentage(pickRate) }} pick
    </UBadge>
    <UBadge
      color="primary"
      size="sm"
    >
      {{ formatPercentage(winRate) }} win
    </UBadge>
  </div>
</template>
