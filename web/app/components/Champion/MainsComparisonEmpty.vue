<script setup lang="ts">
import type { ChampionMainsComparison } from '~~/shared/types/champions'

// Every way the account-vs-mains comparison ends without figures: nothing
// submitted yet, an account or main we don't track, or a sample below the floor.
const props = defineProps<{
  comparison: ChampionMainsComparison | null | undefined
  playerLabel: string
  mainsLabel: string
}>()

/**
 * Every side short of the floor, with its real count. Both can be thin at once,
 * and naming only one sends the user chasing the wrong half of the problem —
 * so this drives a list rather than a single sentence about the player.
 */
const thinSides = computed(() => {
  const short: Array<{ label: string, games: number }> = []
  const player = props.comparison?.player
  const mains = props.comparison?.mains
  if (player && !player.sampleMet) {
    short.push({ label: props.playerLabel, games: player.games })
  }
  if (mains && !mains.sampleMet) {
    short.push({ label: props.mainsLabel, games: mains.games })
  }
  return short
})
</script>

<template>
  <!-- Nothing submitted yet, or a Riot ID the API can't parse. -->
  <UEmpty
    v-if="!comparison"
    size="sm"
    icon="i-lucide-user-round-search"
  >
    <template #description>
      Enter a Riot ID as <span class="whitespace-nowrap text-default">Name#TAG</span> to compare it
      with the players who main this champion.
    </template>
  </UEmpty>

  <!--
    We only compare accounts already in our database — there is no
    on-demand lookup against Riot — so an account we've never ingested is a
    normal answer, not an error. Say so plainly and point at the tracked
    list rather than leaving a dead end.
  -->
  <UEmpty
    v-else-if="comparison.status === 'UNKNOWN_ACCOUNT'"
    size="sm"
    icon="i-lucide-user-round-x"
  >
    <template #description>
      <span class="block font-medium text-highlighted">We don't track this account yet</span>
      The comparison only covers players already in our database.
    </template>
    <template #actions>
      <UButton
        to="/truemains"
        size="sm"
        color="neutral"
        variant="subtle"
        icon="i-lucide-trophy"
        label="Browse the tracked players"
      />
    </template>
  </UEmpty>

  <UEmpty
    v-else-if="comparison.status === 'UNKNOWN_TARGET'"
    size="sm"
    icon="i-lucide-user-round-x"
    description="We don't track the main you picked. Compare against all mains instead."
  />

  <!--
    Below the games floor on one side or the other. The numbers exist but
    would be noise, so the metrics stay hidden and we say exactly how far
    the sample is from the bar rather than showing an unreliable verdict.
  -->
  <UEmpty
    v-else
    size="sm"
    icon="i-lucide-chart-no-axes-column"
  >
    <template #description>
      <span class="block font-medium text-highlighted">Not enough games to compare</span>
      Each side needs {{ comparison.minGames }} recorded
      {{ comparison.minGames === 1 ? 'game' : 'games' }} on this champion.
    </template>

    <!--
      Every thin side is listed, never just the first: when both are short,
      naming only the account would send the user hunting for more of their
      own games when the mains pool is the half that's missing. In `#body`
      rather than the description so it stays a list.
    -->
    <template
      v-if="thinSides.length"
      #body
    >
      <ul class="flex flex-col gap-0.5 text-center">
        <li
          v-for="side in thinSides"
          :key="side.label"
          class="text-sm text-muted"
        >
          <span class="text-default">{{ side.label }}</span>:
          {{ side.games }} {{ side.games === 1 ? 'game' : 'games' }}
        </li>
      </ul>
    </template>
  </UEmpty>
</template>
