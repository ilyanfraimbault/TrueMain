<script setup lang="ts">
import type { BuildVariations } from '~~/shared/types/champions'
import type {
  ChampionStaticData,
  StaticItemData,
  StaticSummonerSpellData,
} from '~~/shared/types/static-data'
import { itemSlots, variationOptions } from '~~/shared/utils/build'
import type { ItemContextCard } from '~~/shared/utils/item-context'
import { itemContextKey } from '~~/shared/utils/item-context'

const props = defineProps<{
  variations: BuildVariations
  championStatic: ChampionStaticData
  itemsMap: Record<number, StaticItemData>
  summonersMap: Record<number, StaticSummonerSpellData>
  /** True while the summoner-spell static map is still loading — see `ChampionCoreSpells`. */
  summonersPending?: boolean
  /** Situational verdicts (#1451), keyed by slot + item. Absent where the page has no slice for them. */
  itemContext?: Map<string, ItemContextCard>
  /** Scaffolding rather than data — see `ChampionBuildTabs`' own `pending`. */
  pending?: boolean
}>()

/**
 * The verdict for one row of a variation list. The slot is part of the key because the
 * same id answers a different question in each: Mercury's Treads as *the boots choice* is
 * not Mercury's Treads as a build item, and only the first has a boots verdict.
 */
function contextFor(slot: 'Boots' | 'Starter', itemId: number): ItemContextCard | undefined {
  return props.itemContext?.get(itemContextKey(slot, itemId))
}

// Only the categories that carry an actual choice (#1466): `variationOptions`
// drops the long tail below the pickrate floor, caps what is left, and returns
// nothing at all when one option dominates — see its own comment for why a lone
// alternative is worse than no card. Each card is then rendered on its list
// being non-empty, so a settled category disappears rather than restating the
// core block under a heading that promises alternatives.
const summonerSpells = computed(() => variationOptions(props.variations.summonerSpells))
const skillOrder = computed(() => variationOptions(props.variations.skillOrder))
const boots = computed(() => variationOptions(props.variations.boots))
const starterItems = computed(() => variationOptions(props.variations.starterItems))

// Nothing to arbitrate anywhere: the section itself goes, rather than leaving a
// gap in the panel's rhythm where four cards used to be.
const hasVariations = computed(() => Boolean(
  summonerSpells.value.length
  || skillOrder.value.length
  || boots.value.length
  || starterItems.value.length,
))

function summonerName(id: number): string {
  return props.summonersMap[id]?.name ?? `Spell ${id}`
}

// Slots, not resolved items — see `itemSlots`: dropping the ids the map cannot
// resolve yet emptied these rows down to their bare pickrate badge while the
// item map was in flight.
function itemsByIds(ids: number[]) {
  return itemSlots(ids, props.itemsMap)
}

function spellByKey(key: string) {
  return props.championStatic.championSpells[key] ?? null
}
</script>

<template>
  <!-- Flex, not a two-column grid: a grid leaves the last card of an odd count
       stranded at half width on a row of its own, which reads as a layout
       accident rather than as a card. Here every card is half-width-ish
       (`basis` + the gap) but allowed to `grow`, so a lone last card fills its
       row instead of orphaning, and `min-w-72` drops the whole thing to one
       column before a card gets too narrow to hold an icon row and its
       badges. -->
  <div
    v-if="hasVariations"
    class="flex flex-wrap gap-4"
  >
    <SectionCard
      v-if="summonerSpells.length"
      :level="2"
      title="Summoner spells"
      class="grow basis-[calc(50%-0.5rem)] min-w-72"
    >
      <ul class="space-y-2">
        <li
          v-for="option in summonerSpells"
          :key="`spells-${option.spell1Id}-${option.spell2Id}`"
          class="flex items-center justify-between gap-3"
        >
          <div class="flex items-center gap-1">
            <GameTooltipSummonerSpellIcon
              v-for="spellId in [option.spell1Id, option.spell2Id]"
              :key="`sum-${option.spell1Id}-${option.spell2Id}-${spellId}`"
              :spell="summonersMap[spellId] ?? null"
              :fallback-label="summonerName(spellId)"
              :pending="summonersPending"
              :width="32"
              :height="32"
              class="size-8 rounded"
            />
          </div>
          <RateBadge
            :games="option.games"
            :pick-rate="option.pickRate"
            :win-rate="option.winRate"
            :pending="pending"
          />
        </li>
      </ul>
    </SectionCard>

    <SectionCard
      v-if="skillOrder.length"
      :level="2"
      title="Skill order"
      class="grow basis-[calc(50%-0.5rem)] min-w-72"
    >
      <ul class="space-y-2">
        <li
          v-for="(option, optionIndex) in skillOrder"
          :key="`skill-${optionIndex}`"
          class="flex items-center justify-between gap-3"
        >
          <div class="flex flex-wrap items-center gap-1">
            <template
              v-for="(key, index) in option.sequence"
              :key="`${optionIndex}-${key}-${index}`"
            >
              <div class="relative size-8">
                <GameTooltipChampionSpellIcon
                  :spell="spellByKey(key)"
                  :fallback-label="key"
                  :pending="pending"
                  :width="32"
                  :height="32"
                  class="size-8 rounded"
                />
                <ItemRankBadge :value="key" />
              </div>
              <UIcon
                v-if="index < option.sequence.length - 1"
                name="i-lucide-chevron-right"
                class="size-3 text-dimmed"
              />
            </template>
          </div>
          <RateBadge
            :games="option.games"
            :pick-rate="option.pickRate"
            :win-rate="option.winRate"
            :pending="pending"
          />
        </li>
      </ul>
    </SectionCard>

    <SectionCard
      v-if="boots.length"
      :level="2"
      title="Boots"
      class="grow basis-[calc(50%-0.5rem)] min-w-72"
    >
      <ul class="space-y-2">
        <li
          v-for="(option, optionIndex) in boots"
          :key="`boots-${optionIndex}-${option.itemIds.join('-')}`"
          class="flex items-center justify-between gap-3"
        >
          <div class="flex items-center gap-1">
            <GameTooltipItemIcon
              v-for="(slot, index) in itemsByIds(option.itemIds)"
              :key="`boots-item-${optionIndex}-${slot.id}-${index}`"
              :item="slot.item"
              :context="contextFor('Boots', slot.id)"
              :width="32"
              :height="32"
              class="size-8 rounded"
            />
          </div>
          <RateBadge
            :games="option.games"
            :pick-rate="option.pickRate"
            :win-rate="option.winRate"
            :pending="pending"
          />
        </li>
      </ul>
    </SectionCard>

    <SectionCard
      v-if="starterItems.length"
      :level="2"
      title="Starter"
      class="grow basis-[calc(50%-0.5rem)] min-w-72"
    >
      <ul class="space-y-2">
        <li
          v-for="(option, optionIndex) in starterItems"
          :key="`starter-${optionIndex}-${option.itemIds.join('-')}`"
          class="flex items-center justify-between gap-3"
        >
          <div class="flex items-center gap-1">
            <GameTooltipItemIcon
              v-for="(slot, index) in itemsByIds(option.itemIds)"
              :key="`starter-item-${optionIndex}-${slot.id}-${index}`"
              :item="slot.item"
              :context="contextFor('Starter', slot.id)"
              :width="32"
              :height="32"
              class="size-8 rounded"
            />
          </div>
          <RateBadge
            :games="option.games"
            :pick-rate="option.pickRate"
            :win-rate="option.winRate"
            :pending="pending"
          />
        </li>
      </ul>
    </SectionCard>
  </div>
</template>
