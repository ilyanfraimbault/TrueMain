<script setup lang="ts">
import type { ProfileRanked } from '~~/shared/types/profile'

const props = defineProps<{
  ranked: ProfileRanked | null
}>()

// Tier colour mapping — picks one accent per tier so the eye locks onto the
// rank at a glance. Emerald palette only per the project style rule, except
// for the warm tiers (Iron/Bronze/Silver/Gold) where rank semantics demand
// the standard colour cues — players read the rank from the colour first.
const TIER_COLORS: Record<string, string> = {
  IRON: 'text-stone-400',
  BRONZE: 'text-amber-700',
  SILVER: 'text-slate-300',
  GOLD: 'text-amber-400',
  PLATINUM: 'text-teal-300',
  EMERALD: 'text-emerald-300',
  DIAMOND: 'text-sky-300',
  MASTER: 'text-fuchsia-300',
  GRANDMASTER: 'text-red-300',
  CHALLENGER: 'text-cyan-200',
}

const tierColor = computed(() => {
  if (!props.ranked) return 'text-muted'
  return TIER_COLORS[props.ranked.tier.toUpperCase()] ?? 'text-default'
})

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
  // Master+ tiers don't have meaningful divisions; Riot still returns "I" for them.
  const upperTier = props.ranked.tier.toUpperCase()
  if (upperTier === 'MASTER' || upperTier === 'GRANDMASTER' || upperTier === 'CHALLENGER') {
    return props.ranked.tier
  }
  return `${props.ranked.tier} ${props.ranked.division}`
})
</script>

<template>
  <section class="rounded-lg bg-elevated/40 px-4 py-3">
    <h2 class="text-xs font-semibold uppercase tracking-wide text-muted">
      Ranked Solo/Duo
    </h2>
    <template v-if="ranked">
      <div class="mt-1 flex items-baseline gap-3">
        <span class="text-2xl font-bold capitalize" :class="tierColor">
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
