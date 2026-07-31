<script setup lang="ts">
import type { ChampionStaticListItem } from '~~/shared/types/static-data'
import type { ChampionMatchupEntry } from '~~/shared/types/champions'
import { formatPercentage } from '~~/shared/utils/ddragon'
import { formatGoldDiff } from '~/utils/lane-verdict'

const props = defineProps<{
  entry: ChampionMatchupEntry
  opponent: ChampionStaticListItem | null
}>()

// Win rate above / below even — the universal green / red read.
const winRateClass = computed(() =>
  props.entry.winRate >= 0.5 ? 'text-emerald-400' : 'text-red-400',
)

// Lane win rate (#919) sits beside the game win rate, but it is a different
// measurement over a different denominator and must not read as a second opinion
// on the same one: it counts only *decided* lanes — a gold gap past the threshold
// at 15 minutes — so its sample is always smaller than `games`, and can be zero
// while `games` is not.
//
// A dash, never 0%, when there is nothing to say: no decided lane in this slice,
// or the live single-opponent search path, which has no lane data behind it at
// all. The tooltip carries the real decided-lane count, so the figure can never be
// read as resting on `games`.
const laneWinRateLabel = computed(() =>
  props.entry.laneWinRate === null ? '—' : formatPercentage(props.entry.laneWinRate, 0),
)
const laneWinRateClass = computed(() => {
  if (props.entry.laneWinRate === null) return 'text-dimmed'
  return props.entry.laneWinRate >= 0.5 ? 'text-emerald-400' : 'text-red-400'
})
// The gold gap rides in the same tooltip (#976): it is the magnitude the rate
// cannot carry — 60% of lanes won by 120 gold and by 1200 are the same rate —
// but it rests on its own, smaller sample, so it is spelled out rather than
// squeezed into a second column that would read as a qualifier of the first.
const laneTooltip = computed(() => {
  const { laneWinRate, decidedLaneGames, averageGoldDiffAt15, goldDiffLaneGames } = props.entry
  const gap = averageGoldDiffAt15 === null
    ? null
    : `avg ${formatGoldDiff(averageGoldDiffAt15)} gold at 15 min over `
      + `${goldDiffLaneGames.toLocaleString()} lane(s)`
  if (laneWinRate === null) {
    return gap ?? 'No lane decided past the gold threshold at 15 min in this slice'
  }
  const rate = `Lane win rate over ${decidedLaneGames.toLocaleString()} decided lane(s)`
  return gap ? `${rate} · ${gap}` : rate
})
</script>

<template>
  <div class="flex items-center gap-3 rounded-md px-2 py-1.5 transition-colors hover:bg-elevated/40">
    <SkeletonImage
      v-if="opponent?.iconUrl"
      :src="opponent.iconUrl"
      :alt="opponent.name"
      width="32"
      height="32"
      class="size-8 shrink-0 rounded"
    />
    <div v-else class="size-8 shrink-0 rounded bg-elevated" aria-hidden="true" />
    <span class="min-w-0 flex-1 truncate text-sm text-default">
      {{ opponent?.name ?? `Champion ${entry.opponentChampionId}` }}
    </span>
    <span class="shrink-0 text-xs tabular-nums text-muted">
      {{ entry.games.toLocaleString() }} games
    </span>
    <UTooltip :text="laneTooltip">
      <span
        class="w-12 shrink-0 text-right text-sm font-medium tabular-nums"
        :class="laneWinRateClass"
      >{{ laneWinRateLabel }}</span>
    </UTooltip>
    <span
      class="w-12 shrink-0 text-right text-sm font-semibold tabular-nums"
      :class="winRateClass"
    >
      {{ formatPercentage(entry.winRate, 0) }}
    </span>
  </div>
</template>
