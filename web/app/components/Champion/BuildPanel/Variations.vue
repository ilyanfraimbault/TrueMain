<script setup lang="ts">
import type { BuildVariations } from '~~/shared/types/champions'
import type {
  ChampionStaticData,
  StaticItemData,
  StaticSummonerSpellData,
} from '~~/shared/types/static-data'

const props = defineProps<{
  variations: BuildVariations
  championStatic: ChampionStaticData
  itemsMap: Record<number, StaticItemData>
  summonersMap: Record<number, StaticSummonerSpellData>
}>()

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
    <SectionCard title="Summoner spells">
      <ul class="space-y-2">
        <li
          v-for="option in variations.summonerSpells"
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
          v-if="!variations.summonerSpells.length"
          class="text-sm text-muted"
        >
          No data
        </li>
      </ul>
    </SectionCard>

    <SectionCard title="Skill order">
      <ul class="space-y-2">
        <li
          v-for="(option, optionIndex) in variations.skillOrder"
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
                <span class="absolute bottom-0 left-1/2 inline-flex h-3 min-w-3 -translate-x-1/2 items-center justify-center rounded bg-default/85 px-0.5 text-[8px] font-bold uppercase ring-1 ring-default backdrop-blur-sm">
                  {{ key }}
                </span>
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
          v-if="!variations.skillOrder.length"
          class="text-sm text-muted"
        >
          No data
        </li>
      </ul>
    </SectionCard>

    <SectionCard title="Boots">
      <ul class="space-y-2">
        <li
          v-for="(option, optionIndex) in variations.boots"
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
          v-if="!variations.boots.length"
          class="text-sm text-muted"
        >
          No data
        </li>
      </ul>
    </SectionCard>

    <SectionCard title="Starter">
      <ul class="space-y-2">
        <li
          v-for="(option, optionIndex) in variations.starterItems"
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
          v-if="!variations.starterItems.length"
          class="text-sm text-muted"
        >
          No data
        </li>
      </ul>
    </SectionCard>
  </div>
</template>
