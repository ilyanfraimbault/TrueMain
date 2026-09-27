<script setup lang="ts">
import type { DraftCandidate } from '~/types/draft'
import type { TierEntry } from '~/composables/useTierList'

/**
 * One candidate pick, poster-sized: its score over the card, the champion's
 * loading art, and — on the card in focus — its lane numbers from the tier
 * list. The score is the draft endpoint's own sum of two measured deltas
 * (matchup against the lane opponent, synergy with the locked allies), in win
 * rate points; the tooltip says which half carries it.
 */
const props = defineProps<{
  candidate: DraftCandidate
  entry: TierEntry | null
  focused: boolean
  /** Height of the card in the podium, 0..1 of the row. */
  height: number
  /** The score measures something; otherwise the card leads with the lane win rate. */
  measured: boolean
}>()

const { nameOf } = useChampionStatics()

const points = (delta: number) => `${delta >= 0 ? '+' : '−'}${Math.abs(delta * 100).toFixed(1)}`

const tone = computed(() => {
  if (!props.measured) return winRateTone(props.entry?.winRate ?? null)
  if (props.candidate.thinSample) return 'text-dimmed'
  return props.candidate.score >= 0 ? 'text-data-good' : 'text-data-bad'
})

const why = computed(() => {
  if (!props.measured) return props.entry ? `Win rate on this lane: ${percent(props.entry.winRate)} over ${props.entry.games.toLocaleString('en-US')} games` : undefined
  const { matchupDelta, matchupGames, synergyDelta, synergyGames, thinSample } = props.candidate
  const parts = [
    matchupGames > 0 ? `Matchup ${points(matchupDelta)}% over ${matchupGames.toLocaleString('en-US')} games` : 'No matchup games yet',
    synergyGames > 0 ? `Synergy ${points(synergyDelta)}% over ${synergyGames.toLocaleString('en-US')} games` : null,
    thinSample ? 'Few games behind this score' : null,
  ]
  return parts.filter(Boolean).join(' · ')
})

const percent = (value: number) => `${(value * 100).toFixed(1)}%`
</script>

<template>
  <div class="flex h-full w-[112px] shrink-0 flex-col justify-end gap-1.5" :title="why">
    <p class="text-center font-bold tabular-nums leading-none tracking-tight" :class="[tone, focused ? 'text-[26px]' : 'text-[22px]']">
      <template v-if="measured">{{ points(candidate.score) }}<span class="text-xs">%</span></template>
      <template v-else-if="entry">{{ (entry.winRate * 100).toFixed(1) }}<span class="text-xs">% WR</span></template>
    </p>

    <div
      class="relative w-full overflow-hidden rounded-lg bg-elevated transition-[box-shadow]"
      :class="focused ? 'ring-2 ring-primary shadow-[0_0_24px_-8px_var(--color-rosegold-400)]' : 'ring-1 ring-white/5'"
      :style="{ height: `${Math.round(height * 100)}%` }"
    >
      <ChampionArt :champion-id="candidate.championId" kind="loading" fade="none" position="50% 12%" :class="candidate.thinSample && 'opacity-70'" />
      <div class="absolute inset-0 bg-gradient-to-t from-ink-950 via-ink-950/40 via-30% to-transparent to-60%" />

      <div class="absolute inset-x-0 bottom-0 flex flex-col gap-1.5 p-2">
        <p class="truncate text-[13px] font-semibold text-highlighted">{{ nameOf(candidate.championId) }}</p>
        <div v-if="focused && entry" class="grid grid-cols-2 gap-1">
          <div class="flex flex-col">
            <span class="stat-label text-[9px]!">Win rate</span>
            <span class="stat-value text-xs">{{ percent(entry.winRate) }}</span>
          </div>
          <div class="flex flex-col">
            <span class="stat-label text-[9px]!">Pick rate</span>
            <span class="stat-value text-xs">{{ percent(entry.pickRate) }}</span>
          </div>
        </div>
        <UBadge v-if="measured && candidate.thinSample" color="neutral" variant="soft" size="sm" class="self-start">Few games</UBadge>
      </div>
      <TierMark v-if="entry" :tier="entry.tier" class="absolute left-2 top-2 scale-75 origin-top-left" />
    </div>
  </div>
</template>
