<script setup lang="ts">
import type { ProfilePositionStat } from '~~/shared/types/profile'
import { getPositionIconUrl } from '~~/shared/utils/ddragon'

const props = defineProps<{
  positions: ProfilePositionStat[]
}>()

// Render positions in standard lane order regardless of the API's ordering
// — TOP / JUNGLE / MIDDLE / BOTTOM / UTILITY left-to-right reads as a
// "scoreboard" rather than a sorted ranking.
const LANE_ORDER = ['TOP', 'JUNGLE', 'MIDDLE', 'BOTTOM', 'UTILITY'] as const

const sortedPositions = computed(() => {
  const byPosition = new Map(props.positions.map(p => [p.position, p]))
  return LANE_ORDER.map(position => ({
    position,
    games: byPosition.get(position)?.games ?? 0,
    rate: byPosition.get(position)?.rate ?? 0,
  }))
})

const totalGames = computed(() => sortedPositions.value.reduce((sum, p) => sum + p.games, 0))

function widthPercent(games: number): string {
  if (totalGames.value === 0) return '0%'
  return `${(games / totalGames.value) * 100}%`
}
</script>

<template>
  <section v-if="totalGames > 0" class="flex flex-col gap-3">
    <h2 class="text-xs font-semibold uppercase tracking-wide text-muted">
      Preferred positions
    </h2>
    <div
      class="flex h-2 overflow-hidden rounded-full bg-elevated/40"
      role="img"
      :aria-label="`Position distribution: ${totalGames} games across ${sortedPositions.filter(p => p.games > 0).length} lanes`"
    >
      <div
        v-for="lane in sortedPositions"
        :key="lane.position"
        class="bg-emerald-500/70 first:rounded-l-full last:rounded-r-full"
        :style="{ width: widthPercent(lane.games) }"
        :class="{ 'opacity-30': lane.games === 0 }"
      />
    </div>
    <div class="grid grid-cols-5 gap-2 text-xs text-muted">
      <div
        v-for="lane in sortedPositions"
        :key="lane.position"
        class="flex flex-col items-center gap-0.5"
      >
        <NuxtImg
          :src="getPositionIconUrl(lane.position)"
          :alt="lane.position"
          class="size-5"
          width="20"
          height="20"
        />
        <span class="tabular-nums">{{ Math.round(lane.rate * 100) }}%</span>
      </div>
    </div>
  </section>
</template>
