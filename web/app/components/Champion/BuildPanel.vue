<script setup lang="ts">
import type { ChampionBuild } from '~~/shared/types/champions'
import type {
  ChampionStaticData,
  RuneTreeResponse,
  StaticItemData,
  StaticSummonerSpellData,
} from '~~/shared/types/static-data'

defineProps<{
  build: ChampionBuild
  championStatic: ChampionStaticData
  itemsMap: Record<number, StaticItemData>
  summonersMap: Record<number, StaticSummonerSpellData>
  runeTree: RuneTreeResponse | null
}>()
</script>

<template>
  <div class="space-y-6">
    <!-- Section 1: Core view. Flattened to a bare block (no UCard) — the whole
         panel now lives inside the single enveloping card from BuildTabs, so a
         card here would nest card-in-card. -->
    <div>
      <!-- Outer grid: left column is flexible, right Runes column is a fixed
           240 px so the left column never resizes when rune layouts change
           between builds/positions. 240 px fits the widest primary tree
           (4 keystones, e.g. Precision): 4 × 35 px keystones + 3 × 2 px gaps =
           146 px, + 24 px gutter + 68 px secondary/shard column (3 × 20 px +
           2 × 4 px gaps) = 238 px, plus a 2 px safety margin. Trees with only
           3 keystones leave a little trailing space — the trade-off for a
           stable, non-shifting layout (sizing to content would shift the left
           column when switching builds/positions). -->
      <div class="grid gap-x-6 gap-y-5 lg:grid-cols-[minmax(0,1fr)_240px]">
        <!-- Section A: everything except runes -->
        <div class="flex flex-col gap-5 sm:flex-row sm:items-start">
          <!-- A1: Summoners + Starter, stacked, left-aligned.
               Width is the wider of the two cards (116 px for Starter). -->
          <div class="flex flex-col gap-5">
            <ChampionCoreSpells
              :summoners="build.core.summonerSpells"
              :summoners-map="summonersMap"
            />
            <ChampionCoreStarterItems
              :starter="build.core.starterItems"
              :items-map="itemsMap"
            />
          </div>
          <!-- A2: Skill order + Boots side-by-side, Build path below.
               A2 grows to fill the remainder of Section A. justify-around
               distributes the two fixed-width cards evenly inside A2. -->
          <div class="flex flex-1 flex-col gap-5">
            <!-- A2a: Skill order (156 px) and Boots (76 px) evenly spaced -->
            <div class="flex flex-wrap items-start justify-around gap-6">
              <ChampionCoreSkillOrder
                :skill-order="build.core.skillOrder"
                :champion-static="championStatic"
              />
              <ChampionCoreBoots
                :boots="build.core.boots"
                :items-map="itemsMap"
              />
            </div>
            <!-- A2b: Build path (336 px from sm) centered in A2 -->
            <div class="flex justify-center">
              <ChampionCoreBuildPath
                :path="build.core.itemPath"
                :items-map="itemsMap"
              />
            </div>
          </div>
        </div>
        <!-- Runes column — fixed 240 px wrapper at lg+ keeps the left column
             stable in the two-column layout. Below lg the core view is a single
             column, so the wrapper stays full-width to avoid regressing mobile.
             The wrapper is always present (even with no rune data) so the grid
             track doesn't collapse and cause a reflow. -->
        <div class="w-full shrink-0 overflow-hidden lg:w-[240px]">
          <ChampionCoreRunes
            v-if="build.core.runePage && runeTree"
            :page="build.core.runePage"
            :tree="runeTree"
            :keystone-size="35"
          />
        </div>
      </div>
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
  </div>
</template>
