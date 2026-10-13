<script setup lang="ts">
import type { LeaderboardTopChampion } from '#shared/types/leaderboard'
import type { ChampionStaticListItem } from '#shared/types/static-data'
import type { LeaderboardMainBuild } from '#common/composables/useLeaderboardChampions'

// A truemain's champions column: the signature champion's build cluster (icon,
// play rate, keystone, first item) in a fixed 9rem slot, and the two sub-mains
// from @5xl. With `portrait`, a narrow table shows the signature champion's
// portrait alone and the cluster from @4xl. The host sets the display and the
// column's width; a player without a main leaves the column empty.
defineProps<{
  main: LeaderboardMainBuild | null
  topChampions: readonly LeaderboardTopChampion[]
  championsById: Map<number, ChampionStaticListItem>
  /** The player's slug: every champion links to their page on it. */
  nameTag: string
  portrait?: boolean
}>()
</script>

<template>
  <div class="items-center justify-center gap-3">
    <template v-if="main">
      <ChampionLink
        v-if="portrait && main.iconUrl"
        :champion-id="main.champion.championId"
        :name="main.name"
        :icon-url="main.iconUrl"
        :name-tag="nameTag"
        :title="`${main.name} · ${main.champion.games} games`"
        class="size-7 @4xl:hidden"
      />
      <!-- 9rem: 30px icon + 40px play rate + two 22px build icons + the three
           0.5rem gaps between them, i.e. the cluster at its widest. -->
      <div class="w-36 shrink-0 justify-center" :class="portrait ? 'hidden @4xl:flex' : 'flex'">
        <LeaderboardChampionBuild v-bind="main" reserve-slots loading="lazy" />
      </div>
      <LeaderboardSubChampions
        :top-champions="topChampions"
        :champions-by-id="championsById"
        :name-tag="nameTag"
        class="hidden @5xl:flex"
      />
    </template>
  </div>
</template>
