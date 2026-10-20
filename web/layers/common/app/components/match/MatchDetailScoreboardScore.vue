<script setup lang="ts">
import type { MatchDetailParticipant } from '#shared/types/match-detail'
import { ordinal } from '#common/utils/ordinal'

// A scoreboard player's performance score (#639) and placement. The score,
// placement and accolades come from the API, or on the desktop app from its own
// port of the model for a game only the client has (#2075) — nothing is
// computed here.
// MVP = crown (best of the winning side) in the brand's genuine-gold accent;
// ACE = rosette (best of the losing side) in the rose `primary`. A game never
// graded (placement 0) keeps the column, empty, rather than ranking ten
// players 0th.
const props = defineProps<{
  participant: MatchDetailParticipant
}>()

// Four steps inside the brand rose-gold (`primary`) palette: the better the
// game, the denser the pill. Sub-50 stays neutral so an average row doesn't shout.
const pillClass = computed(() => {
  const score = props.participant.performanceScore
  if (score >= 80) return 'bg-primary/20 text-primary ring-primary/50'
  if (score >= 65) return 'bg-primary/12 text-primary ring-primary/30'
  if (score >= 50) return 'bg-primary/8 text-primary ring-primary/25'
  return 'bg-default/50 text-muted ring-default'
})
</script>

<template>
  <div class="flex w-[3.25rem] shrink-0 flex-col items-end gap-0.5">
    <template v-if="participant.placement > 0">
      <UTooltip :text="`Performance score ${participant.performanceScore}/100`">
        <span
          class="inline-flex items-center gap-1 rounded px-1.5 py-0.5 text-[11px] font-bold leading-none tabular-nums ring-1"
          :class="pillClass"
        >
          <UIcon
            v-if="participant.isMvp || participant.isAce"
            :name="participant.isMvp ? 'i-lucide-crown' : 'i-lucide-award'"
            class="size-3"
            :class="participant.isMvp ? 'text-gold' : 'text-primary'"
            :aria-label="participant.isMvp ? 'MVP' : 'ACE'"
          />
          {{ participant.performanceScore }}
        </span>
      </UTooltip>
      <span class="text-[10px] leading-none text-muted tabular-nums">
        {{ ordinal(participant.placement) }}
      </span>
    </template>
  </div>
</template>
