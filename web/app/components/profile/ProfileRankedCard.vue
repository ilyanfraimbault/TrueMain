<script setup lang="ts">
import type { ProfileRanked } from '~~/shared/types/profile'
import { formatTier, tierColor } from '~/utils/tiers'

const props = defineProps<{
  ranked: ProfileRanked | null
}>()

const tierClass = computed(() => tierColor(props.ranked?.tier))

const winRateLabel = computed(() => {
  if (!props.ranked || props.ranked.winRate === null) return null
  return `${Math.round(props.ranked.winRate * 100)}% WR`
})

const recordLabel = computed(() => {
  if (!props.ranked) return null
  const w = props.ranked.wins
  const l = props.ranked.losses
  if (w === null && l === null) return null
  return `${w ?? '?'}W ${l ?? '?'}L`
})

const displayTier = computed(() => {
  if (!props.ranked) return null
  return formatTier(props.ranked.tier, props.ranked.division)
})
</script>

<template>
  <section class="rounded-lg bg-elevated/40 px-4 py-3">
    <h2 class="text-xs font-semibold uppercase tracking-wide text-muted">
      Ranked Solo/Duo
    </h2>
    <template v-if="ranked">
      <div class="mt-1 flex items-baseline gap-3">
        <span class="text-2xl font-bold capitalize" :class="tierClass">
          {{ displayTier?.toLowerCase() }}
        </span>
        <span class="text-lg font-semibold tabular-nums">
          {{ ranked.leaguePoints }} LP
        </span>
      </div>
      <p v-if="recordLabel || winRateLabel" class="mt-1 text-sm text-muted">
        <span v-if="recordLabel" class="tabular-nums">{{ recordLabel }}</span>
        <span v-if="recordLabel && winRateLabel"> · </span>
        <span v-if="winRateLabel" class="tabular-nums">{{ winRateLabel }}</span>
      </p>
    </template>
    <template v-else>
      <p class="mt-1 text-base text-muted">
        Unranked
      </p>
    </template>
  </section>
</template>
