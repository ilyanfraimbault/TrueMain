<script setup lang="ts">
import type { ChampionResponse } from '~~/shared/types/champions'
import type {
  ChampionStaticData,
  ChampionStaticListItem,
  RuneTreeResponse,
  StaticItemData,
  StaticSummonerSpellData,
} from '~~/shared/types/static-data'
import type { ChampionPosition } from '#common/utils/positions'

// The player-scoped champion page's middle column, resilient by design: a
// champion the profile lists as a main must always show *something* on click.
// The build breakdown renders whenever the backend holds any aggregate for the
// player (however thin); when it holds none (`notEnoughData` — no
// timeline-complete ranked game on record) a soft notice replaces it, and either
// way the player's recent games on the champion render below (a separate data
// source that has the raw matches even when the build aggregate doesn't).
const props = defineProps<{
  nameTag: string
  championId: number
  champion: ChampionResponse | null | undefined
  error: unknown
  staleError: unknown
  notEnoughData: boolean
  /** The SSR-resolved champion name, for the banner's h1. */
  championName: string | null
  /** The statics' display name, for inline copy. */
  displayName: string | null
  displayIconUrl: string | null
  splashUrl: string | null
  position: ChampionPosition | null
  playerLabel: string
  globalBuildPath: string
  filterLabels: string[]
  staticData: ChampionStaticData | null | undefined
  staticList: ChampionStaticListItem[] | null | undefined
  itemsMap: Record<number, StaticItemData> | null | undefined
  summonersMap: Record<number, StaticSummonerSpellData> | null | undefined
  summonersPending: boolean
  runeTree: RuneTreeResponse | null | undefined
}>()

const emit = defineEmits<{ retry: [], clear: [] }>()

// Thin-sample caution: the backend builds from any number of games, flagging
// small samples with minSampleMet=false. Surfaced as a warning icon by the title
// so a handful of games reads as a rough personal signal, not a meta build.
const lowSampleMessage = computed(() => {
  if (!props.champion || props.champion.minSampleMet)
    return null
  const games = props.champion.totalGames
  return `Only ${games} ${games === 1 ? 'game' : 'games'} on record — this build is inferred from a small personal sample, `
    + 'so treat it as a rough signal rather than a reliable recommendation.'
})
</script>

<template>
  <div class="order-2 min-w-0 space-y-6 xl:col-start-2 xl:row-start-1 xl:row-span-3">
    <FetchErrorAlert
      v-if="error"
      :error="error"
      title="Failed to load this champion"
    />

    <template v-else>
      <!-- The champion page's banner (#2026): the player's games and win
           rate as its sample line, no figures row (the trend is every rank).
           The SSR-resolved name; skeletons until the aggregate lands, but an
           empty slice is settled and says so. -->
      <ChampionHero
        :champion-name="championName"
        :champion-icon-url="displayIconUrl"
        :champion-id="championId"
        :splash-url="splashUrl"
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

      <template v-else>
        <ChampionBuildTabs
          v-if="champion && staticData"
          :builds="champion.builds"
          :champion-static="staticData"
          :items-map="itemsMap ?? {}"
          :summoners-map="summonersMap ?? {}"
          :summoners-pending="summonersPending"
          :rune-tree="runeTree ?? null"
        />
        <ChampionBuildTabsSkeleton v-else />
      </template>

      <!-- This player's recent games on this champion — rendered even when the
           build breakdown above is absent (the degraded state), so clicking a
           main always surfaces their actual games. -->
      <ChampionPlayerMatchHistory
        :name-tag="nameTag"
        :champion-id="championId"
        :champion-name="displayName"
        :champions="staticList"
        :items="itemsMap"
        :summoner-spells="summonersMap"
        :rune-tree="runeTree"
      />
    </template>
  </div>
</template>
