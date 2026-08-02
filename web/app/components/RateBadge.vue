<script setup lang="ts">
import { formatPercentage } from '~~/shared/utils/ddragon'

// `games` is what the percentages are computed from. It used to sit next to the
// badges (#923) because a matchup slice is usually small — measured on
// production, the median champion-vs-opponent pair holds 4 games on a patch — so
// "100% win" must stay readable as "1 of 1". It now lives in the hover tooltip
// instead: the sample size is one gesture away, and the row keeps a single
// compact pick/win chip pair.
const props = defineProps<{
  pickRate: number
  winRate: number
  games?: number
}>()

const tooltipText = computed(() => {
  const parts: string[] = []

  if (props.games !== undefined) {
    parts.push(`${props.games.toLocaleString('en-US')} ${props.games === 1 ? 'game' : 'games'}`)
  }

  parts.push(`${formatPercentage(props.pickRate)} pick rate`, `${formatPercentage(props.winRate)} win rate`)

  return parts.join(' · ')
})
</script>

<template>
  <UTooltip
    :text="tooltipText"
    :delay-duration="150"
  >
    <div class="flex shrink-0 items-center gap-2 whitespace-nowrap">
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
  </UTooltip>
</template>
