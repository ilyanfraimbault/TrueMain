<script setup lang="ts">
import type { ChampionResponse } from '~~/shared/types/champions'
import type {
  ChampionStaticData,
  RuneTreeResponse,
  StaticItemData,
  StaticSummonerSpellData,
} from '~~/shared/types/static-data'
import type { PositionFilter } from '#common/utils/positions'

// The player-scoped champion page's build, resilient by design: with no
// aggregate for the player (`notEnoughData`) a soft notice replaces it. The
// build tabs skeleton until champion + statics land (#834).
const props = defineProps<{
  championId: number
  champion: ChampionResponse | null | undefined
  staleError: unknown
  notEnoughData: boolean
  /** The SSR-resolved champion name, for the heading. */
  championName: string | null
  /** The statics' display name, for inline copy. */
  displayName: string | null
  displayIconUrl: string | null
  position: PositionFilter | null
  playerLabel: string
  globalBuildPath: string
  filterLabels: string[]
  staticData: ChampionStaticData | null | undefined
  itemsMap: Record<number, StaticItemData> | null | undefined
  summonersMap: Record<number, StaticSummonerSpellData> | null | undefined
  summonersPending: boolean
  runeTree: RuneTreeResponse | null | undefined
}>()

const emit = defineEmits<{ retry: [], clear: [] }>()

// Thin-sample caution: the backend builds from any number of games, flagging
// small samples with minSampleMet=false. Surfaced as a warning dot by the title
// so a handful of games reads as a rough personal signal, not a meta build.
const lowSampleMessage = computed(() => {
  if (!props.champion || props.champion.minSampleMet)
    return null
  const games = props.champion.totalGames
  return `Only ${games} ${games === 1 ? 'game' : 'games'} on record`
})
</script>

<template>
  <!-- The SSR-resolved name; skeletons until the aggregate lands, but
       an empty slice is settled and says so. -->
  <ChampionPlayerHeading
    :champion-name="championName"
    :champion-icon-url="displayIconUrl"
    :champion-id="championId"
    :position="champion?.position || position || ''"
    :total-games="champion?.totalGames ?? 0"
    :total-wins="champion?.totalWins ?? null"
    :low-sample-message="lowSampleMessage"
    :loading="!champion && !notEnoughData"
  />

  <StaleContentNotice
    :error="staleError"
    subject="the previous slice"
    :on-retry="() => emit('retry')"
  />

  <ChampionPlayerBuildEmpty
    v-if="notEnoughData"
    :player-label="playerLabel"
    :champion-name="displayName"
    :global-build-path="globalBuildPath"
    :filter-labels="filterLabels"
    @clear="emit('clear')"
  />
  <ChampionBuildTabs
    v-else-if="champion && staticData"
    :builds="champion.builds"
    :champion-static="staticData"
    :items-map="itemsMap ?? {}"
    :summoners-map="summonersMap ?? {}"
    :summoners-pending="summonersPending"
    :rune-tree="runeTree ?? null"
  />
  <ChampionBuildTabsSkeleton v-else />
</template>
