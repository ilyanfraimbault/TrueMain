<script setup lang="ts">
import type {
  BuildItemPath,
  BuildItemSet,
  BuildRunePage,
  BuildSkillOrder,
  BuildSummonerSpells,
} from '~~/shared/types/champions'
import type {
  ChampionStaticData,
  RuneTreeResponse,
  StaticItemData,
  StaticSummonerSpellData,
} from '~~/shared/types/static-data'

/**
 * The core view of a build — summoners, starter, skill order, boots, item path,
 * runes — and nothing else. Three call sites render exactly this block from
 * three different aggregations (the champion page's build, the matchup
 * recommendation, the standard-build fallback), so it lives here rather than
 * being re-laid-out by hand in each of them.
 *
 * Dimensions are passed one by one rather than as a `build`: the composition
 * recommendation carries them flat and has no `core` object to hand over.
 */
withDefaults(defineProps<{
  summonerSpells: BuildSummonerSpells | null
  starterItems: BuildItemSet | null
  skillOrder: BuildSkillOrder | null
  boots: BuildItemSet | null
  itemPath: BuildItemPath | null
  runePage: BuildRunePage | null
  /**
   * Null while the champion's static data is still in flight — only the skill
   * order needs it, so the rest of the block renders without waiting on it.
   */
  championStatic: ChampionStaticData | null
  itemsMap: Record<number, StaticItemData>
  summonersMap: Record<number, StaticSummonerSpellData>
  runeTree: RuneTreeResponse | null
  /** Keystone row size (px) — see `ChampionCoreRunes`. */
  keystoneSize?: number
  /**
   * Shown in the runes column when the aggregation produced no page. Null keeps
   * the column silently empty, which is right where runes are simply one panel
   * among many; a sampled build says so, because there the absence is a fact
   * about the sample.
   */
  noRunesMessage?: string | null
}>(), {
  keystoneSize: undefined,
  noRunesMessage: null,
})
</script>

<template>
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
          :summoners="summonerSpells"
          :summoners-map="summonersMap"
        />
        <ChampionCoreStarterItems
          :starter="starterItems"
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
            v-if="championStatic"
            :skill-order="skillOrder"
            :champion-static="championStatic"
          />
          <ChampionCoreBoots
            :boots="boots"
            :items-map="itemsMap"
          />
        </div>
        <!-- A2b: Build path (336 px from sm) centered in A2 -->
        <div class="flex justify-center">
          <ChampionCoreBuildPath
            :path="itemPath"
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
        v-if="runePage && runeTree"
        :page="runePage"
        :tree="runeTree"
        :keystone-size="keystoneSize"
      />
      <p
        v-else-if="noRunesMessage"
        class="text-sm text-muted"
      >
        {{ noRunesMessage }}
      </p>
    </div>
  </div>
</template>
