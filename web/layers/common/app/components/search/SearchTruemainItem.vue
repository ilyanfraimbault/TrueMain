<script setup lang="ts">
import type { SearchResult } from '#shared/types/search'
import type { ChampionStaticListItem } from '#shared/types/static-data'
import { getProfileIconUrl } from '#shared/utils/ddragon'
import { leaderboardChampionIcon, leaderboardChampionName } from '#common/utils/leaderboard-row'

// A truemain in the search palette (`AppSearch`): the icon, the Riot ID with the
// region flag under it, then the lanes, the mains and the rank crest — the
// leaderboard row's columns at palette size. It fills the palette's whole row
// (the `#truemain` slot). The identity is drawn here rather than through
// `Account`, the decided exception (web-frontend-rules): the palette re-fills
// the same row DOM on every keystroke and wants `SkeletonImage`, which blanks
// between two pictures.
const props = defineProps<{
  result: SearchResult
  /** Data Dragon patch for the icon; null until the version list has loaded. */
  patch: string | null
  championsById: ReadonlyMap<number, ChampionStaticListItem>
}>()

// Mains the static list has not resolved yet (still loading) are left out
// rather than drawn as broken pictures.
const mains = computed(() => props.result.topChampionIds
  .map(id => ({ id, name: leaderboardChampionName(props.championsById, id), iconUrl: leaderboardChampionIcon(props.championsById, id) }))
  .filter(main => main.iconUrl !== null))
</script>

<template>
  <span class="flex min-w-0 flex-1 items-center gap-1.5">
    <SearchItemAvatar :src="getProfileIconUrl(result.identity.profileIconId, patch)" />

    <!-- `text-highlighted` on the name is not decoration: the palette's item
         is `text-dimmed`, and only its default label span (which this slot
         replaces) brightens the text again, as it does on the champion rows. -->
    <span class="flex min-w-0 flex-1 flex-col items-start gap-0.5">
      <span class="flex min-w-0 max-w-full items-baseline gap-1">
        <span class="truncate text-highlighted">{{ result.identity.gameName }}</span>
        <span v-if="result.identity.tagLine" class="shrink-0 text-dimmed">#{{ result.identity.tagLine }}</span>
      </span>
      <LeaderboardRegionFlag :region="result.region" :width="16" />
    </span>

    <!-- The crest alone: the tier name stays in its tooltip, never spelled out. -->
    <span class="ms-auto flex shrink-0 items-center gap-3">
      <LeaderboardLanes :positions="result.positions" class="flex" />
      <span v-if="mains.length" class="flex items-center gap-1">
        <SkeletonImage
          v-for="main in mains"
          :key="main.id"
          :src="main.iconUrl"
          :alt="main.name"
          :title="main.name"
          class="size-5 shrink-0 rounded"
        />
      </span>
      <RankEmblem v-if="result.ranked" :rank="result.ranked" :size="22" crest-only />
    </span>
  </span>
</template>
