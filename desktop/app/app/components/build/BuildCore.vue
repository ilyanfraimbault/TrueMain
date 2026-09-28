<script setup lang="ts">
import type { BuildItemPath, BuildItemSet, BuildRunePage, BuildSkillOrder, BuildSummonerSpells } from '~~/shared/types/champions'
import type { ChampionStaticData, RuneTreeResponse, StaticItemData, StaticSummonerSpellData } from '~~/shared/types/static-data'
import { itemSlots } from '~~/shared/utils/build'

/**
 * The core of a build — summoners, skill order, starter, boots, build path,
 * runes — laid out for the app's build pane rather than for a site page. The
 * site's `Champion/BuildPanel/Core` only puts the runes beside the rest from a
 * 768 px container; the pane is ~560 px, so there the runes fell under
 * everything and pushed the build tree off screen. Here the rest is a 2 × 2
 * grid over the build path, drawn at 30 px, and the runes stand to its right,
 * centred on its height, so the tree starts right under both.
 *
 * Every icon is the site's own — tooltips, the Q/W/E badge, the rune block —
 * only the arrangement and the size are the app's.
 */
const props = withDefaults(defineProps<{
  summonerSpells: BuildSummonerSpells | null
  starterItems: BuildItemSet | null
  skillOrder: BuildSkillOrder | null
  boots: BuildItemSet | null
  itemPath: BuildItemPath | null
  runePage: BuildRunePage | null
  /** Null while the champion's spells are loading: the skill order keeps its boxes meanwhile. */
  championStatic: ChampionStaticData | null
  itemsMap: Record<number, StaticItemData>
  summonersMap: Record<number, StaticSummonerSpellData>
  summonersPending?: boolean
  runeTree: RuneTreeResponse | null
  /** Said in the runes column when the sample carried no rune page. */
  noRunesMessage?: string | null
}>(), { summonersPending: false, noRunesMessage: null })

const ICON = 30

const spells = computed(() => (props.summonerSpells ? [props.summonerSpells.spell1Id, props.summonerSpells.spell2Id] : []))
const skills = computed(() => props.skillOrder?.sequence ?? [])
const starter = computed(() => itemSlots(props.starterItems?.itemIds, props.itemsMap))
const bootSlots = computed(() => itemSlots(props.boots?.itemIds, props.itemsMap))
const path = computed(() => itemSlots(props.itemPath?.itemIds, props.itemsMap))
</script>

<template>
  <div class="@container">
    <div class="grid gap-x-6 gap-y-4 @[33rem]:grid-cols-[auto_minmax(0,1fr)]">
      <div class="flex flex-col gap-3">
        <div class="grid grid-cols-[auto_auto] justify-start gap-x-5 gap-y-3">
          <section>
            <h2 class="text-xs font-medium text-muted">Summoners</h2>
            <div class="mt-1.5 flex h-[30px] items-center gap-1">
              <GameTooltipSummonerSpellIcon
                v-for="spellId in spells"
                :key="`sum-${spellId}`"
                :spell="summonersMap[spellId] ?? null"
                :pending="summonersPending"
                :settled="!summonersPending"
                :width="ICON"
                :height="ICON"
                class="size-[30px] shrink-0 rounded"
              />
              <span v-if="!spells.length" class="text-xs text-muted">No data</span>
            </div>
          </section>

          <section>
            <h2 class="text-xs font-medium text-muted">Skill order</h2>
            <div class="mt-1.5 flex h-[30px] items-center gap-0.5">
              <template v-for="(key, index) in skills" :key="`${key}-${index}`">
                <div class="relative size-[30px] shrink-0">
                  <GameTooltipChampionSpellIcon
                    :spell="championStatic?.championSpells[key] ?? null"
                    :pending="!championStatic"
                    :settled="!!championStatic"
                    :width="ICON"
                    :height="ICON"
                    class="size-[30px] rounded"
                  />
                  <ItemRankBadge :value="key" />
                </div>
                <UIcon v-if="index < skills.length - 1" name="i-lucide-chevron-right" class="size-3.5 shrink-0 text-dimmed" />
              </template>
              <span v-if="!skills.length" class="text-xs text-muted">No data</span>
            </div>
          </section>

          <section>
            <h2 class="text-xs font-medium text-muted">Starter</h2>
            <div class="mt-1.5 flex h-[30px] items-center gap-1">
              <GameTooltipItemIcon
                v-for="(slot, index) in starter"
                :key="`starter-${slot.id}-${index}`"
                :item="slot.item"
                :width="ICON"
                :height="ICON"
                class="size-[30px] shrink-0 rounded"
              />
              <span v-if="!starter.length" class="text-xs text-muted">No data</span>
            </div>
          </section>

          <section>
            <h2 class="text-xs font-medium text-muted">Boots</h2>
            <div class="mt-1.5 flex h-[30px] items-center gap-1">
              <GameTooltipItemIcon
                v-for="(slot, index) in bootSlots"
                :key="`boots-${slot.id}-${index}`"
                :item="slot.item"
                :width="ICON"
                :height="ICON"
                class="size-[30px] shrink-0 rounded"
              />
              <span v-if="!bootSlots.length" class="text-xs text-muted">No data</span>
            </div>
          </section>
        </div>

        <section>
          <h2 class="text-xs font-medium text-muted">Build path</h2>
          <div class="mt-1.5 flex h-[30px] items-center gap-0.5">
            <template v-for="(slot, index) in path" :key="`bp-${slot.id}-${index}`">
              <GameTooltipItemIcon :item="slot.item" :width="ICON" :height="ICON" class="size-[30px] shrink-0 rounded" />
              <UIcon v-if="index < path.length - 1" name="i-lucide-chevron-right" class="size-3.5 shrink-0 text-dimmed" />
            </template>
            <span v-if="!path.length" class="text-xs text-muted">No data</span>
          </div>
        </section>
      </div>

      <!-- The site's rune block at the site's own size and spacing, centred against the column beside it. -->
      <div class="flex min-w-0 items-center justify-center overflow-hidden">
        <ChampionCoreRunes v-if="runePage && runeTree" :page="runePage" :tree="runeTree" :size="36" :keystone-size="39" />
        <p v-else-if="!runePage && noRunesMessage" class="text-sm text-muted">{{ noRunesMessage }}</p>
      </div>
    </div>
  </div>
</template>
