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
    <!-- Section 1: Core view — small per-topic cards on the left, Runes pinned right -->
    <div class="grid gap-4 lg:grid-cols-[minmax(0,1fr)_max-content]">
      <div class="grid gap-4 sm:grid-cols-2">
        <ChampionCoreSpells
          :summoners="build.core.summonerSpells"
          :summoners-map="summonersMap"
        />
        <ChampionCoreSkillOrder
          :skill-order="build.core.skillOrder"
          :champion-static="championStatic"
        />
        <ChampionCoreStarterItems
          :starter="build.core.starterItems"
          :items-map="itemsMap"
        />
        <ChampionCoreBoots
          :boots="build.core.boots"
          :items-map="itemsMap"
        />
        <ChampionCoreBuildPath
          class="sm:col-span-2"
          :path="build.core.itemPath"
          :items-map="itemsMap"
        />
      </div>
      <SectionCard
        v-if="build.core.runePage && runeTree"
        title="Runes"
      >
        <ChampionCoreRunes
          :page="build.core.runePage"
          :tree="runeTree"
          :keystone-size="35"
        />
      </SectionCard>
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
