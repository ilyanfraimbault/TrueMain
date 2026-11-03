<script setup lang="ts">
import { LOW_SAMPLE_GAMES } from '#common/utils/sample-floor'

// The matchup slice's sample, said where the build is read (#2355). Picking an
// opponent re-slices every build section from the games against it, with no
// pick-rate floor, and the median pair holds a handful of games on a patch: a core
// build from four games must not read like the default build. Always the count;
// under `LOW_SAMPLE_GAMES` the warning dot beside the opponent's name.
const props = defineProps<{
  games: number
  opponentName: string
}>()

const countedGames = computed(() => `${props.games} ${props.games === 1 ? 'game' : 'games'}`)
const lowSampleMessage = computed(() =>
  props.games < LOW_SAMPLE_GAMES ? `Only ${countedGames.value} against ${props.opponentName}` : null)
</script>

<template>
  <div class="flex flex-wrap items-center gap-x-1.5 text-sm text-muted">
    <span>Build against {{ opponentName }}</span>
    <WarningDot v-if="lowSampleMessage" :text="lowSampleMessage" />
    <span v-else aria-hidden="true">·</span>
    <span class="tabular-nums">{{ countedGames }}</span>
  </div>
</template>
