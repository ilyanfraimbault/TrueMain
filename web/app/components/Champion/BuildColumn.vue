<script setup lang="ts">
import type { ChampionResponse } from '~~/shared/types/champions'
import type {
  ChampionStaticData,
  RuneTreeResponse,
  StaticItemData,
  StaticSummonerSpellData,
} from '~~/shared/types/static-data'
import type { ChampionPosition } from '#common/utils/positions'
import type { ChampionPanelSnapshots } from '~/composables/useChampionPanelSnapshots'

// The champion page's main column: the build tabs, then the below-the-fold
// charts. Only the build tabs wait for real data; the charts skeleton themselves.
defineProps<{
  champion: ChampionResponse | null | undefined
  staticData: ChampionStaticData | null | undefined
  itemsMap: Record<number, StaticItemData> | null | undefined
  summonersMap: Record<number, StaticSummonerSpellData> | null | undefined
  summonersPending: boolean
  runeTree: RuneTreeResponse | null | undefined
  championId: number
  /** The resolved lane the build tabs and charts follow. */
  position: string | null
  /** The lane picker's value, which the synergies are scoped by. */
  selectedPosition: ChampionPosition | null
  patch: string | null
  eloBracket: string
  opponentChampionId: number | null
  /** A filter change in flight: the tabs show their cold-load skeleton. */
  loading: boolean
  snapshots: ChampionPanelSnapshots
}>()
</script>

<template>
  <div class="min-w-0 space-y-6">
    <ChampionBuildTabs
      v-if="champion && staticData && !loading"
      :builds="champion.builds"
      :champion-static="staticData"
      :items-map="itemsMap ?? {}"
      :summoners-map="summonersMap ?? {}"
      :summoners-pending="summonersPending"
      :rune-tree="runeTree ?? null"
      :champion-id="championId"
      :position="position"
      :patch="patch"
      :elo-bracket="eloBracket"
      :opponent-champion-id="opponentChampionId"
    />
    <ChampionBuildTabsSkeleton v-else />

    <!--
      Everything below the build tabs is below the fold and pulls the
      heavy charting bundle (nuxt-charts). Lazy-load each so its JS lands
      in its own chunk and only downloads/hydrates once scrolled into
      view — keeps the champion detail route's initial JS lean (#820).
      Props come from the `…Snapshot` bundles (frozen at their
      SSR-matching value until `@vue:mounted` reveals the live data) so
      the deferred hydration doesn't mismatch (#834/#837); `rune-tree`,
      `from-patch` and `to-patch` are bound directly since they're
      SSR-safe/locally-stable and don't need freezing.
    -->
    <LazyChampionTrendChart
      hydrate-on-visible
      v-bind="snapshots.trendSnapshot.value"
      @vue:mounted="snapshots.trendSnapshot.reveal"
    />

    <LazyChampionScalingChart
      hydrate-on-visible
      v-bind="snapshots.scalingSnapshot.value"
      @vue:mounted="snapshots.scalingSnapshot.reveal"
    />

    <!--
      Duo/trio synergies (#922). In the main column rather than the
      sidebar: the rows carry four columns plus a lane filter, and picking
      a partner expands a second list underneath.
    -->
    <LazyChampionSynergies
      hydrate-on-visible
      :champion-id="championId"
      :position="selectedPosition"
      :patch="patch"
      :elo-bracket="eloBracket"
      v-bind="snapshots.synergiesSnapshot.value"
      @vue:mounted="snapshots.synergiesSnapshot.reveal"
    />
  </div>
</template>
