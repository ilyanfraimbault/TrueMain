<script setup lang="ts">
import type { ItemContextCard } from '~~/shared/utils/item-context'
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
  /** True while `summonersMap` is still loading — see `ChampionCoreSpells`. */
  summonersPending?: boolean
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
  /**
   * The situational build context (#1451), keyed by `itemContextKey(slot, itemId)` and
   * fetched once by `ChampionBuildTabs`. Empty on the surfaces that do not have a
   * population slice to read it for (the builder preview, the player-scoped page).
   */
  itemContext?: Map<string, ItemContextCard>
  /** Scaffolding rather than data — see `ChampionBuildTabs`' own `pending`. */
  pending?: boolean
}>()

const showPowerspikes = computed(() => Boolean(props.championId && props.position))

// Power spikes are only meaningful within one build, so the panel fetches its
// own slice for the build it renders. BuildTabs keeps every panel mounted, so
// this fires once per build rather than on every tab switch.
// The build key is zeroed while scaffolding: the placeholder aggregate's item
// and keystone ids are made up, and the composable holds (rather than firing a
// request for a build that does not exist) as soon as one of them is 0.
const { data: powerspikes, status: powerspikesStatus } = useChampionPowerspikes(
  () => props.championId ?? 0,
  () => props.position,
  () => props.patch,
  () => (props.pending ? 0 : props.build.firstItemId),
  () => (props.pending ? 0 : props.build.primaryKeystoneId),
  () => props.eloBracket,
  () => props.opponentChampionId,
)
</script>

<template>
  <!--
    The panel answers first and nuances after (#1466). Core plus the build tree
    is the whole answer — what to buy, and the shape of the path that gets there
    — so nothing sits between them. Everything below is the alternatives, in
    descending order of how often a reader is actually arbitrating them.
  -->
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
        :item-context="itemContext"
        :summoners-map="summonersMap"
        :summoners-pending="summonersPending"
        :rune-tree="runeTree"
        :keystone-size="35"
        :pending="pending"
      />
    </div>

    <!-- Section 2: Build tree, directly under the core it illustrates. Padding
         rather than a margin, because the panel's `space-y-6` already owns the
         children's top margin and would win: the tree's first node is drawn
         flush against the top of its box, so without this it sits nearly
         against the core block now that the card that used to stand it off is
         gone. -->
    <ChampionBuildPanelBuildTree
      class="pt-6"
      :tree="build.buildTree"
      :first-item-id="build.firstItemId"
      :item-path="build.core.itemPath?.itemIds ?? []"
      :items-map="itemsMap"
      :item-context="itemContext"
    />

    <!-- Section 3: Variations — only the categories that carry a choice -->
    <ChampionBuildPanelVariations
      :variations="build.variations"
      :item-context="itemContext"
      :champion-static="championStatic"
      :items-map="itemsMap"
      :summoners-map="summonersMap"
      :summoners-pending="summonersPending"
      :pending="pending"
    />

    <!-- Section 4: Rune pages variations -->
    <ChampionBuildPanelRuneList
      v-if="runeTree"
      :rune-pages="build.runePages"
      :rune-tree="runeTree"
      :pending="pending"
    />

    <!-- Section 5: Power spikes for this build -->
    <ChampionBuildPanelPowerspikes
      v-if="showPowerspikes"
      :events="powerspikes?.events ?? []"
      :matchup-scoped="Boolean(opponentChampionId)"
      :items-map="itemsMap"
      :loading="pending || isLoadingStatus(powerspikesStatus)"
    />
  </div>
</template>
