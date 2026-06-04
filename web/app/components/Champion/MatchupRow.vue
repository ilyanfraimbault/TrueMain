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
    <span
      class="w-12 shrink-0 text-right text-sm font-semibold tabular-nums"
      :class="winRateClass"
    >
      {{ formatPercentage(entry.winRate, 0) }}
    </span>
  </div>
</template>
