<script setup lang="ts">
import type { LiveTeam } from '#shared/types/live'
import type { ChampionStaticListItem, RuneTreeResponse, StaticSummonerSpellData } from '#shared/types/static-data'

// One side of a live game (#2103): its bans, then its five players in lane order.
const props = defineProps<{
  team: LiveTeam
  championsById: ReadonlyMap<number, ChampionStaticListItem>
  summonerSpells: Record<number, StaticSummonerSpellData> | null
  runeTree: RuneTreeResponse | null
}>()

const isBlue = computed(() => props.team.teamId === 100)
</script>

<template>
  <section
    class="glass flex min-w-0 flex-col gap-2 rounded-xl p-3"
    :aria-label="isBlue ? 'Blue side' : 'Red side'"
  >
    <header class="flex items-center justify-between gap-2 px-1">
      <h3 class="flex items-center gap-2 text-xs font-semibold uppercase tracking-wide text-muted">
        <span
          class="size-2 rounded-full"
          :class="isBlue ? 'bg-sky-400' : 'bg-red-400'"
        />
        {{ isBlue ? 'Blue side' : 'Red side' }}
      </h3>
      <ul
        v-if="team.bans.length"
        class="flex items-center gap-1"
        aria-label="Bans"
      >
        <li
          v-for="ban in team.bans"
          :key="ban"
        >
          <SkeletonImage
            :src="championsById.get(ban)?.iconUrl ?? null"
            :alt="`Banned ${championsById.get(ban)?.name ?? ban}`"
            :title="`Banned: ${championsById.get(ban)?.name ?? ban}`"
            width="20"
            height="20"
            loading="lazy"
            class="size-5 rounded opacity-50 grayscale"
          />
        </li>
      </ul>
    </header>

    <ul class="flex flex-col gap-1">
      <LiveGameParticipant
        v-for="(participant, index) in team.participants"
        :key="`${participant.championId}-${index}`"
        :participant="participant"
        :champion="championsById.get(participant.championId) ?? null"
        :summoner-spells="summonerSpells"
        :rune-tree="runeTree"
      />
    </ul>
  </section>
</template>
