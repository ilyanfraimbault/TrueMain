<script setup lang="ts">
import { isLoadingStatus } from '~/utils/async-data'
import type { ChampionBuild } from '~~/shared/types/champions'
import type {
  ChampionStaticData,
  RuneTreeResponse,
  StaticItemData,
  StaticSummonerSpellData,
} from '~~/shared/types/static-data'

const props = defineProps<{
  build: ChampionBuild
  championStatic: ChampionStaticData
  itemsMap: Record<number, StaticItemData>
  summonersMap: Record<number, StaticSummonerSpellData>
  runeTree: RuneTreeResponse | null
  // Optional population scope — absent in the builder preview and on the
  // player-scoped champion page, where power spikes are not shown.
  championId?: number
  position?: string | null
  patch?: string | null
  eloBracket?: string | null
  // Lane opponent selected in the filter bar (#957): the build this panel renders
  // is already the matchup's, so its spikes must come from the same games.
  opponentChampionId?: number | null
}>()

const showPowerspikes = computed(() => Boolean(props.championId && props.position))

// Power spikes are only meaningful within one build, so the panel fetches its
// own slice for the build it renders. BuildTabs keeps every panel mounted, so
// this fires once per build rather than on every tab switch.
const { data: powerspikes, status: powerspikesStatus } = useChampionPowerspikes(
  () => props.championId ?? 0,
  () => props.position,
  () => props.patch,
  () => props.build.firstItemId,
  () => props.build.primaryKeystoneId,
  () => props.eloBracket,
  () => props.opponentChampionId,
)
</script>

<template>
  <div class="space-y-6">
    <!-- Section 1: Core view. Flattened to a bare block (no UCard) — the whole
         panel now lives inside the single enveloping card from BuildTabs, so a
         card here would nest card-in-card. -->
    <div>
      <ChampionBuildPanelCore
        :summoner-spells="build.core.summonerSpells"
        :starter-items="build.core.starterItems"
        :skill-order="build.core.skillOrder"
        :boots="build.core.boots"
        :item-path="build.core.itemPath"
        :rune-page="build.core.runePage"
        :champion-static="championStatic"
        :items-map="itemsMap"
        :summoners-map="summonersMap"
        :rune-tree="runeTree"
        :keystone-size="35"
      />
    </div>

    <!-- Section 2: Variations -->
    <ChampionBuildPanelVariations
      :variations="build.variations"
      :champion-static="championStatic"
      :items-map="itemsMap"
      :summoners-map="summonersMap"
    />

    <!-- Section 3: Build tree -->
    <ChampionBuildPanelBuildTree
      :tree="build.buildTree"
      :first-item-id="build.firstItemId"
      :item-path="build.core.itemPath?.itemIds ?? []"
      :items-map="itemsMap"
    />

    <!-- Section 4: Rune pages variations -->
    <ChampionBuildPanelRuneList
      v-if="runeTree"
      :rune-pages="build.runePages"
      :rune-tree="runeTree"
    />

    <!-- Section 5: Power spikes for this build -->
    <ChampionBuildPanelPowerspikes
      v-if="showPowerspikes"
      :events="powerspikes?.events ?? []"
      :matchup-scoped="Boolean(opponentChampionId)"
      :items-map="itemsMap"
      :loading="isLoadingStatus(powerspikesStatus)"
    />
  </div>
</template>
