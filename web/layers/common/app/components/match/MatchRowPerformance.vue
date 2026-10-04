<script setup lang="ts">
import type { MatchSummarySelf } from '#shared/types/matches'
import { ordinal } from '#common/utils/ordinal'

// Performance score (#918) of a match-history row, in the right-edge slot it
// shares with the MVP/ACE accolade derived from it: a crowned game shows the
// crown (`gold`) or the ACE rosette (`primary`, as in the scoreboard) with the
// score in its tooltip, any other game prints the graded score. Fixed width so
// the columns line up down the list.
const props = defineProps<{
  self: MatchSummarySelf
}>()

const perfScore = computed(() => props.self.performanceScore)

const accolade = computed(() => {
  if (props.self.isMvp) return 'MVP'
  if (props.self.isAce) return 'ACE'
  return null
})

const perfTooltip = computed(() => {
  const detail = `Performance score ${perfScore.value}/100 — ${ordinal(props.self.placement)} of 10 in this game`
  return accolade.value ? `${accolade.value} · ${detail}` : detail
})

// Graded on the same three-tone ramp as the KDA ratio (`MatchRowKda`), and on
// the same thresholds the model's own bands imply: 50 is "average on every
// available component", so a standout has to clear it comfortably.
const perfColor = computed(() => {
  if (perfScore.value >= 75) return 'text-gold'
  if (perfScore.value >= 60) return 'text-data-good'
  return 'text-muted'
})
</script>

<template>
  <div class="flex w-7 shrink-0 items-center justify-center">
    <UTooltip :text="perfTooltip">
      <UIcon
        v-if="accolade"
        :name="self.isMvp ? 'i-lucide-crown' : 'i-lucide-award'"
        class="size-5 drop-shadow"
        :class="self.isMvp ? 'text-gold' : 'text-primary'"
        :aria-label="`${accolade}, performance score ${perfScore}`"
      />
      <span
        v-else
        class="text-sm font-bold tabular-nums"
        :class="perfColor"
        :aria-label="`Performance score ${perfScore}`"
      >
        {{ perfScore }}
      </span>
    </UTooltip>
  </div>
</template>
