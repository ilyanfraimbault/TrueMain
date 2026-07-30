<script setup lang="ts">
import type { ChampionStaticListItem } from '~~/shared/types/static-data'
import type { ChampionMatchupEntry } from '~~/shared/types/champions'
import { formatPercentage } from '~~/shared/utils/ddragon'

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
const laneTooltip = computed(() =>
  props.entry.laneWinRate === null
    ? 'No lane decided past the gold threshold at 15 min in this slice'
    : `Lane win rate over ${props.entry.decidedLaneGames.toLocaleString()} decided lane(s)`,
)
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
