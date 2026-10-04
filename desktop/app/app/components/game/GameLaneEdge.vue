<script setup lang="ts">
import type { LaneEdge } from '~/utils/lane-edge'

/**
 * The arrow between a lane's two players (#1863): towards the side the lane
 * favours — ours on the left — with that side's chance; doubled from a clear
 * edge on. What it starts from is in its tooltip.
 */
const props = defineProps<{ edge: LaneEdge | null | undefined }>()

/** From this chance on the arrow doubles. */
const CLEAR_EDGE = 60

const icon = computed(() => {
  const edge = props.edge
  if (!edge || edge.side === 'even') return 'i-lucide-equal'
  const strong = edge.percent >= CLEAR_EDGE
  if (edge.side === 'ally') return strong ? 'i-lucide-chevrons-left' : 'i-lucide-chevron-left'
  return strong ? 'i-lucide-chevrons-right' : 'i-lucide-chevron-right'
})

const tone = computed(() => {
  const side = props.edge?.side
  return side === 'ally' ? 'text-ally' : side === 'enemy' ? 'text-enemy' : 'text-dimmed'
})

const tooltip = computed(() => {
  const edge = props.edge
  if (!edge) return ''
  const start = edge.matchupGames
    ? `Matchup ${Math.round(edge.matchup * 100)}% over ${edge.matchupGames.toLocaleString()} games`
    : 'No matchup data: starts even'
  return `${start}, moved by each player's last games on their champion — win rate, gold, CS and XP at 15 minutes.`
})
</script>

<template>
  <UTooltip v-if="edge" :text="tooltip">
    <div class="flex flex-col items-center leading-none" :class="tone">
      <UIcon :name="icon" class="size-5" />
      <span class="mt-0.5 text-[11px] font-semibold tabular-nums">{{ edge.side === 'even' ? '50' : edge.percent }}%</span>
    </div>
  </UTooltip>
</template>
