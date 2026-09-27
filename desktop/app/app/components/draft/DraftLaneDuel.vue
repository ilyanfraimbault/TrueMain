<script setup lang="ts">
import type { CompositionLane } from '~/types/build'
import { LANE_LABELS, laneIconUrl } from '~/types/draft'
import type { Lane } from '~/types/draft'
import { formatGoldDiff, laneVerdict, winRateTone } from '~/utils/lane-verdict'

/**
 * Between the two teams: the lane the build on screen is for — its champion
 * against the one it faces — and how that lane goes, from the gold gap at 15
 * over the games behind the build. Where the reference apps draw a radar of
 * team traits we have no measurement for, this is one we do.
 */
const props = defineProps<{
  championId: number | null
  opponentId: number | null
  position: string | null
  lane: CompositionLane | null
  pending: boolean
}>()

const { nameOf, portraitOf } = useChampionStatics()

const noun = computed(() => (props.position === 'JUNGLE' ? 'matchup' : 'lane'))
const verdict = computed(() => laneVerdict(props.lane?.averageGoldDiffAt15 ?? null, props.lane?.measuredGames ?? 0, noun.value))
const laneLabel = computed(() => (props.position && props.position in LANE_LABELS ? LANE_LABELS[props.position as Lane] : null))
</script>

<template>
  <div class="flex flex-col items-center justify-center gap-2 text-center">
    <div class="flex items-center gap-1.5">
      <img v-if="position" :src="laneIconUrl(position)" alt="" class="size-4 opacity-80">
      <span class="stat-label">{{ laneLabel ?? 'Draft' }}</span>
    </div>

    <div class="flex items-center gap-2">
      <div class="size-11 overflow-hidden rounded-full bg-elevated ring-2 ring-primary/70">
        <img v-if="championId && portraitOf(championId)" :src="portraitOf(championId)!" :alt="nameOf(championId)" class="size-full scale-110 object-cover">
      </div>
      <span class="text-[10px] font-semibold uppercase tracking-widest text-dimmed">vs</span>
      <div class="flex size-11 items-center justify-center overflow-hidden rounded-full bg-elevated ring-2" :class="opponentId ? 'ring-gold/70' : 'ring-default'">
        <img v-if="opponentId && portraitOf(opponentId)" :src="portraitOf(opponentId)!" :alt="nameOf(opponentId)" class="size-full scale-110 object-cover">
        <UIcon v-else name="i-lucide-help-circle" class="size-5 text-dimmed" :title="'Opponent not picked yet'" />
      </div>
    </div>

    <div class="flex min-h-12 flex-col items-center gap-1">
      <UBadge v-if="verdict" :color="verdict.color" :variant="verdict.variant" size="sm">{{ verdict.label }}</UBadge>
      <template v-if="lane && lane.winRate !== null">
        <span class="text-sm font-semibold leading-none tabular-nums" :class="winRateTone(lane.winRate)">{{ Math.round(lane.winRate * 100) }}% <span class="stat-label">lane</span></span>
        <span v-if="lane.averageGoldDiffAt15 !== null" class="text-[10px] tabular-nums text-dimmed">{{ formatGoldDiff(lane.averageGoldDiffAt15) }} gold @15</span>
      </template>
      <UIcon v-else-if="pending" name="i-lucide-loader-circle" class="size-4 text-dimmed" />
    </div>
  </div>
</template>
