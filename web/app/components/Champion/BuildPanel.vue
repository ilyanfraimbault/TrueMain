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
  <div class="space-y-6 pt-4">
    <!-- Section 1: Core view -->
    <UCard>
      <div class="grid gap-x-6 gap-y-5 lg:grid-cols-[minmax(0,1fr)_max-content]">
        <!-- Section A: everything except runes -->
        <div class="flex flex-col gap-5 sm:flex-row sm:items-start">
          <!-- A1: Summoners + Starter, stacked, left-aligned -->
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
          <!-- A2: Skill order + Boots on the same row (pinned to the edges),
               Build path centered below. A2 grows to fill the row so the
               edge-pinning + centring happen on the full available width. -->
          <div class="flex flex-1 flex-col gap-5">
            <!-- A2a: Skill order and Boots evenly spaced across the row -->
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
            <!-- A2b: Build path centered horizontally -->
            <div class="flex justify-center">
              <ChampionCoreBuildPath
                :path="build.core.itemPath"
                :items-map="itemsMap"
              />
            </div>
          </div>
        </div>
        <!-- Runes column -->
        <div v-if="build.core.runePage && runeTree">
          <ChampionCoreRunes
            :page="build.core.runePage"
            :tree="runeTree"
            :keystone-size="35"
          />
        </div>
      </div>
    </UCard>

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
