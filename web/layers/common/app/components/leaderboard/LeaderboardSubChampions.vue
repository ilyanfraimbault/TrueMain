<script setup lang="ts">
import type { LeaderboardTopChampion } from '#shared/types/leaderboard'
import type { ChampionStaticListItem } from '#shared/types/static-data'
import { leaderboardChampionIcon, leaderboardChampionName, subChampionSlots } from '#common/utils/leaderboard-row'

// The sub-mains beside a truemain's signature champion: always two slots, an
// empty box where the player has no such champion — a filled square would
// read as an icon that failed to load. Each links to the player's own page on
// that champion. The host sets the display.
const props = defineProps<{
  topChampions: readonly LeaderboardTopChampion[]
  championsById: Map<number, ChampionStaticListItem>
  /** The player's slug, for the player-scoped champion links. */
  nameTag: string
}>()

const slots = computed(() => subChampionSlots(props.topChampions).map(champion => champion && {
  ...champion,
  name: leaderboardChampionName(props.championsById, champion.championId),
  iconUrl: leaderboardChampionIcon(props.championsById, champion.championId),
}))
</script>

<template>
  <div class="w-[52px] shrink-0 items-center gap-1">
    <template v-for="(champion, index) in slots" :key="champion?.championId ?? `empty-${index}`">
      <ChampionLink
        v-if="champion?.iconUrl"
        :champion-id="champion.championId"
        :name="champion.name"
        :icon-url="champion.iconUrl"
        :name-tag="nameTag"
        :title="`${champion.name} · ${champion.games} games`"
        class="size-6"
      />
      <div v-else class="size-6 shrink-0" aria-hidden="true" />
    </template>
  </div>
</template>
