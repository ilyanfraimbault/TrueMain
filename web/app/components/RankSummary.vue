<script setup lang="ts">
// Rank crest + colored tier/LP + win-loss record, shared between the
// truemains profile card (full-size, above the fold) and the leaderboard
// row's rank-emblem tooltip (compact, on hover) — see `ProfileRankedCard.vue`
// and `LeaderboardRow.vue`.
import { formatPercentage } from '~~/shared/utils/ddragon'
import { formatTier, tierColor } from '~/utils/tiers'

const props = withDefaults(defineProps<{
  tier: string
  division: string
  leaguePoints: number
  wins?: number | null
  losses?: number | null
  winRate?: number | null
  size?: number
}>(), {
  wins: null,
  losses: null,
  winRate: null,
  size: 48,
})

const tierClass = computed(() => tierColor(props.tier))
const tierLabel = computed(() => formatTier(props.tier, props.division))

const recordLabel = computed(() => {
  if (props.wins === null && props.losses === null) return null
  const record = `${props.wins ?? '?'}W – ${props.losses ?? '?'}L`
  const wr = props.winRate === null ? null : formatPercentage(props.winRate, 0)
  return wr ? `${record} (${wr})` : record
})
</script>

<template>
  <div class="flex items-center gap-3">
    <RankIcon :tier="tier" :size="size" />
    <div class="flex min-w-0 flex-col leading-tight">
      <span
        class="font-bold tabular-nums"
        :class="[tierClass, size >= 40 ? 'text-base' : 'text-sm']"
      >
        {{ tierLabel }}
        <span class="text-default">{{ leaguePoints }} LP</span>
      </span>
      <span v-if="recordLabel" class="mt-1 text-sm text-muted tabular-nums">
        {{ recordLabel }}
      </span>
    </div>
  </div>
</template>
