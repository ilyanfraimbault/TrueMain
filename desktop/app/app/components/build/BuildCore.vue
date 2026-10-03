<script setup lang="ts">
import type { BuildItemSet, BuildRunePage, BuildSkillOrder, BuildSummonerSpells } from '#shared/types/champions'
import type { ChampionStaticData, RuneTreeResponse, StaticItemData, StaticSummonerSpellData } from '#shared/types/static-data'

/**
 * The core of a build, from the site's own blocks (`Champion/Core/*`) laid out
 * the way the site's `Champion/BuildPanel/Core` lays them out — summoners over
 * starter, skill order over boots, spread by flex, the runes in a 272 px
 * column beside them — with two app changes:
 *
 * - no build path: the build tree under the core already draws it, item by
 *   item, and in the app's short pane the line said it twice;
 * - the runes go beside the rest from a 36rem container rather than the site's
 *   48rem, since the pane is ~630 px. The two columns are the same height the
 *   site sized them to (148 / 152 px), so neither leaves a hollow under it.
 *
 * The runes carry the import button in their corner (#1678).
 */
withDefaults(defineProps<{
  summonerSpells: BuildSummonerSpells | null
  starterItems: BuildItemSet | null
  skillOrder: BuildSkillOrder | null
  boots: BuildItemSet | null
  runePage: BuildRunePage | null
  /** Null while the champion's spells are loading; the skill order waits for them, as on the site. */
  championStatic: ChampionStaticData | null
  itemsMap: Record<number, StaticItemData>
  summonersMap: Record<number, StaticSummonerSpellData>
  summonersPending?: boolean
  runeTree: RuneTreeResponse | null
  /** Said in the runes column when the sample carried no rune page. */
  noRunesMessage?: string | null
  /** Names the page the import button pushes into the client; no button without it. */
  championName?: string | null
}>(), { summonersPending: false, noRunesMessage: null, championName: null })
</script>

<template>
  <div class="@container">
    <div class="grid gap-x-6 gap-y-5 @xl:grid-cols-[minmax(0,1fr)_272px] @xl:items-center">
      <div class="flex flex-wrap items-start justify-around gap-x-6 gap-y-5">
        <div class="flex flex-col gap-5">
          <ChampionCoreSpells :summoners="summonerSpells" :summoners-map="summonersMap" :summoners-pending="summonersPending" />
          <ChampionCoreStarterItems :starter="starterItems" :items-map="itemsMap" />
        </div>
        <div class="flex flex-col gap-5">
          <ChampionCoreSkillOrder v-if="championStatic" :skill-order="skillOrder" :champion-static="championStatic" />
          <ChampionCoreBoots :boots="boots" :items-map="itemsMap" />
        </div>
      </div>

      <div class="relative w-full shrink-0 overflow-hidden @xl:w-[272px]">
        <ChampionCoreRunes v-if="runePage && runeTree" :page="runePage" :tree="runeTree" :size="36" :keystone-size="39" />
        <BuildRuneImport v-if="runePage && championName !== null" :page="runePage" :champion="championName" class="absolute right-0 top-0" />
        <p v-else-if="!runePage && noRunesMessage" class="text-sm text-muted">{{ noRunesMessage }}</p>
      </div>
    </div>
  </div>
</template>
