<script setup lang="ts">
import type { ChampionBuildSummary } from '~~/shared/types/champion-build-summary'
import type {
  ChampionStaticData,
  RuneTreeResponse,
  StaticItemData,
  StaticSummonerSpellData,
} from '~~/shared/types/static-data'
import type { ChampionPosition } from '#common/utils/positions'
import type { ChampionPanelSnapshots } from '~/composables/useChampionPanelSnapshots'

// The champion page's right sidebar: the champion's truemains and matchups,
// the account-vs-mains comparison and the build in words. Below xl it stacks
// under the main column.
defineProps<{
  championId: number
  position: ChampionPosition | null
  patch: string
  eloBracket: string
  summary: ChampionBuildSummary | null | undefined
  staticData: ChampionStaticData | null | undefined
  itemsMap: Record<number, StaticItemData> | null | undefined
  summonersMap: Record<number, StaticSummonerSpellData> | null | undefined
  runeTree: RuneTreeResponse | null | undefined
  snapshots: ChampionPanelSnapshots
}>()
</script>

<template>
  <aside class="min-w-0 space-y-6">
    <LazyChampionTruemains
      hydrate-on-visible
      :champion-id="championId"
      :rune-tree="runeTree ?? null"
      v-bind="snapshots.truemainsSnapshot.value"
      @vue:mounted="snapshots.truemainsSnapshot.reveal"
    />

    <LazyChampionMatchups
      hydrate-on-visible
      :champion-id="championId"
      :position="position"
      :elo-bracket="eloBracket"
      :patch="patch"
      v-bind="snapshots.matchupsSnapshot.value"
      @vue:mounted="snapshots.matchupsSnapshot.reveal"
    />

    <!--
      Account-vs-mains head-to-head (#528). Not lazily hydrated: it owns a
      form the user types into, pulls no charting bundle, and takes no
      client-only props — so there is no SSR snapshot to freeze and
      nothing to defer.
    -->
    <ChampionMainsComparison
      :champion-id="championId"
      :position="position"
    />

    <!--
      The build in words (#1123). Not lazy: being in the server HTML is the
      point. Last and collapsed (#1466): at the top it read as the same
      build said twice; here it is one click away for the prose reader.
    -->
    <ChampionBuildSummary
      :summary="summary ?? null"
      :items-map="itemsMap"
      :rune-tree="runeTree ?? null"
      :summoners-map="summonersMap"
      :champion-static="staticData ?? null"
    />
  </aside>
</template>
