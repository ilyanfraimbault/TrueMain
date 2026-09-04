<script setup lang="ts">
import {
  PLACEHOLDER_BUILDS,
  PLACEHOLDER_CHAMPION_STATIC,
  PLACEHOLDER_ITEMS_MAP,
  PLACEHOLDER_RUNE_TREE,
  PLACEHOLDER_SUMMONERS_MAP,
} from '~/utils/build-placeholder'

// Loading state for the two call sites that render only the core view and the
// build tree (the composition builder's matchup recommendation and its standard
// build fallback) rather than the champion page's full tab card. Same idea as
// `ChampionBuildTabsSkeleton`: the real components over a placeholder
// aggregate, so the loading picture is the loaded layout with its icons still
// pulsing — see `utils/build-placeholder`.
const placeholder = PLACEHOLDER_BUILDS[0]!
</script>

<template>
  <div
    class="space-y-6"
    inert
    aria-hidden="true"
  >
    <ChampionBuildPanelCore
      pending
      summoners-pending
      :summoner-spells="placeholder.core.summonerSpells"
      :starter-items="placeholder.core.starterItems"
      :skill-order="placeholder.core.skillOrder"
      :boots="placeholder.core.boots"
      :item-path="placeholder.core.itemPath"
      :rune-page="placeholder.core.runePage"
      :champion-static="PLACEHOLDER_CHAMPION_STATIC"
      :items-map="PLACEHOLDER_ITEMS_MAP"
      :summoners-map="PLACEHOLDER_SUMMONERS_MAP"
      :rune-tree="PLACEHOLDER_RUNE_TREE"
    />
    <ChampionBuildPanelBuildTree
      :tree="placeholder.buildTree"
      :first-item-id="placeholder.firstItemId"
      :item-path="placeholder.core.itemPath?.itemIds ?? []"
      :items-map="PLACEHOLDER_ITEMS_MAP"
    />
  </div>
</template>
