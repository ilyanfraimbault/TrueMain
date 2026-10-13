<script setup lang="ts">
import { formatPercentage, getPositionIconUrl } from '#shared/utils/ddragon'
import { positionLabel } from '#common/utils/positions'
import type { CompositionLane } from '~/types/build'
import { winRateTone } from '#common/utils/rate-tone'

/**
 * Between the two teams: the lane the build on screen is for — its champion
 * against the one it faces — and how often that lane is won, over the games
 * behind the build. Where the reference apps draw a radar of team traits we
 * have no measurement for, this is one we do.
 */
const props = defineProps<{
  championId: number | null
  opponentId: number | null
  position: string | null
  lane: CompositionLane | null
  pending: boolean
}>()

const { nameOf, portraitOf } = useChampionStatics()

const laneLabel = computed(() => positionLabel(props.position, { short: true }))
</script>

<template>
  <div class="flex flex-col items-center justify-center gap-2.5 text-center">
    <img v-if="position" :src="getPositionIconUrl(position)" :alt="laneLabel ?? ''" :title="laneLabel ?? undefined" class="size-5 opacity-80">

    <div class="flex items-center gap-2">
      <!-- Ringed like its card on the board: the one champion selected. -->
      <div class="size-12 overflow-hidden rounded-lg bg-elevated ring-2 ring-primary">
        <img v-if="championId && portraitOf(championId)" :src="portraitOf(championId)!" :alt="nameOf(championId)" :title="nameOf(championId)" class="img-skeleton size-full object-cover">
      </div>
      <span class="text-[10px] font-semibold uppercase tracking-widest text-dimmed">vs</span>
      <!-- No opponent yet is an empty slot, not a question: it fills when their laner picks. -->
      <div class="size-12 overflow-hidden rounded-lg bg-elevated ring-2" :class="opponentId ? 'ring-primary/35' : 'ring-default'">
        <img v-if="opponentId && portraitOf(opponentId)" :src="portraitOf(opponentId)!" :alt="nameOf(opponentId)" :title="nameOf(opponentId)" class="img-skeleton size-full object-cover">
      </div>
    </div>

    <div class="flex min-h-10 flex-col items-center gap-1">
      <template v-if="lane && lane.winRate !== null">
        <span class="text-lg font-semibold leading-none tabular-nums" :class="winRateTone(lane.winRate)">{{ formatPercentage(lane.winRate) }}</span>
        <span class="stat-label">Lane win rate</span>
      </template>
      <UIcon v-else-if="pending" name="i-lucide-loader-circle" class="size-4 text-dimmed" />
    </div>
  </div>
</template>
