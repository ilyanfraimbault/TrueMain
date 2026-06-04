<script setup lang="ts">
import type { ChampionStaticListItem } from '~~/shared/types/static-data'
import type { ChampionPosition } from '~/utils/positions'
import { formatPercentage } from '~~/shared/utils/ddragon'

const props = defineProps<{
  championId: number
  position: ChampionPosition | null
  patch: string | null
  champions: ChampionStaticListItem[]
  /** The champion's overall win rate (0..1) on this slice, for the delta. */
  overallWinRate: number | null
  /** When set, scope the matchup to this player's games. */
  nameTag?: string
}>()

const selectedOpponentId = ref<number | null>(null)

// Exclude the champion itself — a "vs yourself" row isn't a useful matchup to
// surface in the picker.
const opponentOptions = computed(() =>
  props.champions.filter(c => c.championId !== props.championId),
)

const {
  data: matchup,
  status,
  notEnoughData,
} = useChampionMatchup(
  () => props.championId,
  () => props.position,
  selectedOpponentId,
  {
    patch: () => props.patch,
    nameTag: () => props.nameTag,
  },
)

const opponent = computed(() =>
  props.champions.find(c => c.championId === selectedOpponentId.value) ?? null,
)

const isLoading = computed(() => selectedOpponentId.value !== null && status.value === 'pending')

// Delta against the champion's overall win rate (issue #90). Null until both
// the matchup and the overall are known.
const delta = computed(() => {
  if (!matchup.value || props.overallWinRate === null) return null
  return matchup.value.winRate - props.overallWinRate
})
</script>

<template>
  <section class="flex flex-col gap-3">
    <header class="flex flex-wrap items-center justify-between gap-2">
      <div class="flex flex-col gap-0.5">
        <h2 class="text-sm font-semibold">
          Matchups
        </h2>
        <p class="text-xs text-muted">
          How this champion does against a chosen lane opponent.
        </p>
      </div>
      <ChampionPicker
        :champions="opponentOptions"
        :champion-id="selectedOpponentId"
        placeholder="Pick an opponent"
        trigger-class="w-56"
        @update:champion-id="value => (selectedOpponentId = value)"
      />
    </header>

    <!-- Before any selection: a quiet prompt rather than an empty box. -->
    <p
      v-if="selectedOpponentId === null"
      class="rounded-lg bg-elevated/40 px-4 py-6 text-center text-sm text-muted"
    >
      Pick an opponent to see the lane matchup.
    </p>

    <USkeleton v-else-if="isLoading" class="h-[68px] w-full rounded-lg" />

    <p
      v-else-if="notEnoughData || !matchup"
      class="rounded-lg bg-elevated/40 px-4 py-6 text-center text-sm text-muted"
    >
      Not enough games against {{ opponent?.name ?? 'this opponent' }} on this lane yet.
    </p>

    <!-- The matchup card: opponent, sample size, win rate + delta vs overall. -->
    <div
      v-else
      class="flex items-center gap-4 rounded-lg bg-elevated/40 px-4 py-3"
    >
      <SkeletonImage
        v-if="opponent?.iconUrl"
        :src="opponent.iconUrl"
        :alt="opponent.name"
        width="48"
        height="48"
        class="size-12 shrink-0 rounded"
      />
      <div class="flex min-w-0 flex-col">
        <span class="truncate text-sm font-medium text-default">
          vs {{ opponent?.name ?? `Champion ${matchup.opponentChampionId}` }}
        </span>
        <span class="text-xs tabular-nums text-muted">
          {{ matchup.games.toLocaleString() }} games
        </span>
      </div>
      <div class="ml-auto flex items-center gap-3">
        <span class="text-xl font-bold tabular-nums text-default">
          {{ formatPercentage(matchup.winRate, 0) }}
        </span>
        <span
          v-if="delta !== null"
          class="inline-flex items-center gap-1 text-xs font-semibold tabular-nums"
          :class="delta >= 0 ? 'text-emerald-400' : 'text-red-400'"
          title="vs the champion's overall win rate"
        >
          <UIcon
            :name="delta >= 0 ? 'i-lucide-trending-up' : 'i-lucide-trending-down'"
            class="size-3.5"
          />
          {{ formatPercentage(Math.abs(delta), 1) }}
        </span>
      </div>
    </div>
  </section>
</template>
