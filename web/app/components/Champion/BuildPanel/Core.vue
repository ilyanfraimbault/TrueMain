<script setup lang="ts">
import type { ItemContextCard } from '~~/shared/utils/item-context'
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
  /**
   * Situational verdicts (#1451) for the three item blocks below. Optional because the
   * surfaces that reuse this core view — the matchup recommendation and its standard-build
   * fallback — pass whatever slice they have, or none at all.
   */
  itemContext?: Map<string, ItemContextCard>
  summonersMap: Record<number, StaticSummonerSpellData>
  /** True while `summonersMap` is still loading — see `ChampionCoreSpells`. */
  summonersPending?: boolean
  runeTree: RuneTreeResponse | null
  /**
   * Rune sizing (px), both defaulted here rather than passed by every call site
   * (#1466): the numbers are a property of *this* layout — they are what makes
   * the rune block as tall as the block beside it — so the skeleton and the two
   * reuse surfaces inherit them instead of each restating a magic 35 that has
   * to be kept in sync by hand. Override only to depart from the core layout.
   */
  runeSize?: number
  keystoneSize?: number
  /**
   * Shown in the runes column when the aggregation produced no page. Null keeps
   * the column silently empty, which is right where runes are simply one panel
   * among many; a sampled build says so, because there the absence is a fact
   * about the sample.
   */
  noRunesMessage?: string | null
  /** Scaffolding rather than data — see `ChampionBuildTabs`' own `pending`. */
  pending?: boolean
}>(), {
  summonersPending: false,
  // Sized to fill the column rather than to overflow it: measured, the block
  // runs ~148 px at 36/39 against the ~148 px of the summoners/skill/build-path
  // side, where 32/35 left ~24 px of the column empty under it. Deliberately
  // sized to just under rather than over — past this the runes drive the row
  // height and the *left* side is the one with dead space, which is the same
  // complaint mirrored. The wrapper width below follows from these.
  runeSize: 36,
  keystoneSize: 39,
  noRunesMessage: null,
  pending: false,
})
</script>

<template>
  <!-- Outer grid: left column is flexible, right Runes column is a fixed
       268 px so the left column never resizes when rune layouts change
       between builds/positions. 268 px fits the widest primary tree
       (4 keystones, e.g. Precision): 4 × 39 px keystones + 3 × 2 px gaps =
       162 px, + 24 px gutter + 80 px secondary/shard column (3 × 24 px +
       2 × 4 px gaps) = 266 px, plus a 2 px safety margin. It was 240 px at
       the old 35 px keystone; the extra 28 px is what the runes grew by to
       stop leaving a quarter of their column empty, and it comes out of the
       flexible left column, which has slack the runes column does not.
       Trees with only 3 keystones leave a little trailing space — the
       trade-off for a stable, non-shifting layout (sizing to content would
       shift the left column when switching builds/positions). -->
  <div class="grid gap-x-6 gap-y-5 lg:grid-cols-[minmax(0,1fr)_268px]">
    <!-- Section A: everything except runes -->
    <div class="flex flex-col gap-5 sm:flex-row sm:items-start">
      <!-- A1: Summoners + Starter, stacked, left-aligned.
           Width is the wider of the two cards (116 px for Starter). -->
      <div class="flex flex-col gap-5">
        <ChampionCoreSpells
          :summoners="summonerSpells"
          :summoners-map="summonersMap"
          :summoners-pending="summonersPending"
        />
        <ChampionCoreStarterItems
          :starter="starterItems"
          :items-map="itemsMap"
          :item-context="itemContext"
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
            :pending="pending"
          />
          <ChampionCoreBoots
            :boots="boots"
            :items-map="itemsMap"
            :item-context="itemContext"
          />
        </div>
        <!-- A2b: Build path (336 px from sm) centered in A2 -->
        <div class="flex justify-center">
          <ChampionCoreBuildPath
            :path="itemPath"
            :items-map="itemsMap"
            :item-context="itemContext"
          />
        </div>
      </div>
    </div>
    <!-- Runes column — fixed 268 px wrapper at lg+ keeps the left column
         stable in the two-column layout. Below lg the core view is a single
         column, so the wrapper stays full-width to avoid regressing mobile.
         The wrapper is always present (even with no rune data) so the grid
         track doesn't collapse and cause a reflow. -->
    <div class="w-full shrink-0 overflow-hidden lg:w-[268px]">
      <ChampionCoreRunes
        v-if="runePage && runeTree"
        :page="runePage"
        :tree="runeTree"
        :size="runeSize"
        :keystone-size="keystoneSize"
      />
      <!-- Gated on `runePage` alone, not on the `v-if` above: `runeTree` is a
           separate static fetch, so keying the message off "we didn't render
           runes" made it claim the sampled games carried no rune page during
           every load. The aggregate's own null is the only thing that answers
           that; while the tree is in flight the column just stays empty. -->
      <p
        v-else-if="!runePage && noRunesMessage"
        class="text-sm text-muted"
      >
        {{ noRunesMessage }}
      </p>
    </div>
  </div>
</template>
