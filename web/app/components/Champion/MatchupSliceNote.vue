<script setup lang="ts">
import { LOW_SAMPLE_GAMES } from '#common/utils/sample-floor'

// The matchup slice's sample, said where the build is read (#2355). Picking an
// opponent re-slices every build section from the games against it, with no
// pick-rate floor, and the median pair holds a handful of games on a patch: a core
// build from four games must not read like the default build. Always the count;
// under `LOW_SAMPLE_GAMES` a warning and the way back to the standard build.
const props = defineProps<{
  games: number
  opponentName: string
}>()

const emit = defineEmits<{ clear: [] }>()

const thin = computed(() => props.games < LOW_SAMPLE_GAMES)
const countedGames = computed(() => `${props.games} ${props.games === 1 ? 'game' : 'games'}`)
</script>

<template>
  <div
    class="flex flex-wrap items-center gap-x-3 gap-y-2 text-sm"
    :class="thin ? 'text-warning' : 'text-muted'"
  >
    <UIcon
      v-if="thin"
      name="i-lucide-triangle-alert"
      class="size-4 shrink-0"
      aria-hidden="true"
    />
    <span v-if="thin">
      Only {{ countedGames }} against {{ opponentName }} — too few to trust this build.
    </span>
    <span v-else>
      Build against {{ opponentName }} · <span class="tabular-nums">{{ countedGames }}</span>
    </span>
    <UButton
      v-if="thin"
      label="See the standard build"
      color="neutral"
      variant="outline"
      size="xs"
      @click="emit('clear')"
    />
  </div>
</template>
