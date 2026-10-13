<script setup lang="ts">
import type { MatchSummarySelf } from '#shared/types/matches'

// K/D/A of a match row with the ratio under it. A fixed width, not a min-width:
// a 12/2/15 game and a 4/8/8 game must take the same box, or every column to
// their right drifts row to row.
const props = defineProps<{
  self: MatchSummarySelf
}>()

const ratio = computed(() => {
  const { kills, deaths, assists } = props.self
  return deaths === 0 ? Infinity : (kills + assists) / deaths
})

const ratioLabel = computed(() => Number.isFinite(ratio.value) ? `${ratio.value.toFixed(2)} KDA` : 'Perfect')

// The data axis' three tones: gold for a standout (Perfect or 5+), the good end
// for a solid game (3+), muted otherwise — gold being the MVP crown's token, a
// standout ratio says what the crown says (design-system decisions, #1237).
const ratioColor = computed(() => {
  if (ratio.value >= 5) return 'text-gold'
  if (ratio.value >= 3) return 'text-data-good'
  return 'text-muted'
})
</script>

<template>
  <div class="w-[4.5rem] shrink-0 leading-tight">
    <p class="flex items-baseline gap-0.5 text-sm font-semibold tabular-nums text-highlighted">
      {{ self.kills }}<span class="text-dimmed">/</span><span class="text-red-400">{{ self.deaths }}</span><span class="text-dimmed">/</span>{{ self.assists }}
    </p>
    <p class="text-[11px] font-medium tabular-nums" :class="ratioColor">{{ ratioLabel }}</p>
  </div>
</template>
