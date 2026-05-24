<script setup lang="ts">
import type { ProfileRanked } from '~~/shared/types/profile'
import { isApexTier } from '~/utils/tiers'

const props = defineProps<{
  ranked: ProfileRanked | null
}>()

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

const showDivision = computed(() => props.ranked !== null && !isApexTier(props.ranked.tier))
</script>

<template>
  <section class="rounded-lg bg-elevated/40 px-4 py-3">
    <h2 class="text-xs font-semibold uppercase tracking-wide text-muted">
      Ranked Solo/Duo
    </h2>
    <template v-if="ranked">
      <div class="mt-2 flex items-center gap-3">
        <RankIcon :tier="ranked.tier" :size="44" />
        <div class="flex flex-col leading-tight">
          <span v-if="showDivision" class="text-xs uppercase tracking-wide text-muted">
            {{ ranked.division }}
          </span>
          <span class="text-lg font-semibold tabular-nums text-default">
            {{ ranked.leaguePoints }} LP
          </span>
        </div>
      </div>
      <p v-if="recordLabel || winRateLabel" class="mt-2 text-sm text-muted">
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
