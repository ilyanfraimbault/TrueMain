<script setup lang="ts">
import type { BuildVariations } from '~~/shared/types/champions'
import type {
  ChampionStaticData,
  StaticItemData,
  StaticSummonerSpellData,
} from '~~/shared/types/static-data'
import { filterByPickRate } from '~~/shared/utils/build'

const props = defineProps<{
  variations: BuildVariations
  championStatic: ChampionStaticData
  itemsMap: Record<number, StaticItemData>
  summonersMap: Record<number, StaticSummonerSpellData>
}>()

// Hide long-tail alternatives below the shared pickrate floor; the empty-state
// placeholder below keys off these filtered lists rather than the raw props.
const summonerSpells = computed(() => filterByPickRate(props.variations.summonerSpells))
const skillOrder = computed(() => filterByPickRate(props.variations.skillOrder))
const boots = computed(() => filterByPickRate(props.variations.boots))
const starterItems = computed(() => filterByPickRate(props.variations.starterItems))

function summonerName(id: number): string {
  return props.summonersMap[id]?.name ?? `Spell ${id}`
}

function itemsByIds(ids: number[]): StaticItemData[] {
  return ids
    .map(id => props.itemsMap[id])
    .filter((item): item is StaticItemData => Boolean(item))
}

function spellByKey(key: string) {
  return props.championStatic.championSpells[key] ?? null
}
</script>

<template>
  <div class="grid gap-4 sm:grid-cols-2">
    <SectionCard :level="2" title="Summoner spells">
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
              :width="32"
              :height="32"
              class="size-8 rounded"
            />
          </div>
          <RateBadge
            :pick-rate="option.pickRate"
            :win-rate="option.winRate"
          />
        </li>
        <li
          v-if="!summonerSpells.length"
          class="text-sm text-muted"
        >
          No data
        </li>
      </ul>
    </SectionCard>

    <SectionCard :level="2" title="Skill order">
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
            :pick-rate="option.pickRate"
            :win-rate="option.winRate"
          />
        </li>
        <li
          v-if="!skillOrder.length"
          class="text-sm text-muted"
        >
          No data
        </li>
      </ul>
    </SectionCard>

    <SectionCard :level="2" title="Boots">
      <ul class="space-y-2">
        <li
          v-for="(option, optionIndex) in boots"
          :key="`boots-${optionIndex}-${option.itemIds.join('-')}`"
          class="flex items-center justify-between gap-3"
        >
          <div class="flex items-center gap-1">
            <GameTooltipItemIcon
              v-for="(item, index) in itemsByIds(option.itemIds)"
              :key="`boots-item-${optionIndex}-${item.id}-${index}`"
              :item="item"
              :width="32"
              :height="32"
              class="size-8 rounded"
            />
          </div>
          <RateBadge
            :pick-rate="option.pickRate"
            :win-rate="option.winRate"
          />
        </li>
        <li
          v-if="!boots.length"
          class="text-sm text-muted"
        >
          No data
        </li>
      </ul>
    </SectionCard>

    <SectionCard :level="2" title="Starter">
      <ul class="space-y-2">
        <li
          v-for="(option, optionIndex) in starterItems"
          :key="`starter-${optionIndex}-${option.itemIds.join('-')}`"
          class="flex items-center justify-between gap-3"
        >
          <div class="flex items-center gap-1">
            <GameTooltipItemIcon
              v-for="(item, index) in itemsByIds(option.itemIds)"
              :key="`starter-item-${optionIndex}-${item.id}-${index}`"
              :item="item"
              :width="32"
              :height="32"
              class="size-8 rounded"
            />
          </div>
          <RateBadge
            :pick-rate="option.pickRate"
            :win-rate="option.winRate"
          />
        </li>
        <li
          v-if="!starterItems.length"
          class="text-sm text-muted"
        >
          No data
        </li>
      </ul>
    </SectionCard>
  </div>
</template>
