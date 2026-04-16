<script setup lang="ts">
import type { ChampionAdvancedResponse, SkillOrderOptionResponse, SummonerSpellOptionResponse } from '~/types/champions'
import type { ChampionStaticData } from '~/types/static-data'
import {
  getStaticSummonerSpell,
  mapItemSetToStaticItems,
  mapSkillSequenceToStaticSpells,
  sortItemsByGoldDesc
} from '~/utils/champion-display'

const props = defineProps<{
  advanced: ChampionAdvancedResponse
  championStatic: ChampionStaticData
  isStaticPending: boolean
}>()

const visibleSummonerOptions = computed(() =>
  props.advanced.summonerSpellOptions.filter((option) => option.playRate >= 0.1))

const visibleSkillOrderOptions = computed(() =>
  props.advanced.skillOrderOptions.filter((option) => option.playRate >= 0.1))

const starterItemOptions = computed(() =>
  props.advanced.starterItemOptions
    .filter((option) => option.playRate >= 0.1)
    .map((option) => ({
      option,
      items: sortItemsByGoldDesc(mapItemSetToStaticItems(option, props.championStatic.items))
    })))

function summonerPair(option: SummonerSpellOptionResponse) {
  return {
    left: getStaticSummonerSpell(props.championStatic.summonerSpells, option.spell1Id),
    right: getStaticSummonerSpell(props.championStatic.summonerSpells, option.spell2Id)
  }
}

function skillSequence(option: SkillOrderOptionResponse) {
  return mapSkillSequenceToStaticSpells(option.sequence, props.championStatic.championSpells)
}
</script>

<template>
  <section class="space-y-4">
    <h2 class="text-xl font-semibold">
      Advanced details
    </h2>

    <div
      v-if="isStaticPending"
      class="grid gap-4 xl:grid-cols-3"
    >
      <UCard
        v-for="card in 3"
        :key="`advanced-skeleton-${card}`"
        variant="subtle"
      >
        <template #header>
          <h3 class="text-base font-semibold">
            {{ ['Summoner options', 'Skill options', 'Starter item options'][card - 1] }}
          </h3>
        </template>

        <div class="space-y-4">
          <div
            v-for="row in 2"
            :key="row"
            class="flex items-center justify-between gap-3"
          >
            <div class="flex items-center gap-1">
              <USkeleton class="size-10 rounded-md" />
              <USkeleton class="size-10 rounded-md" />
              <USkeleton
                v-if="card !== 1"
                class="size-10 rounded-md"
              />
            </div>
            <div class="flex gap-2">
              <USkeleton class="h-7 w-16 rounded-full" />
              <USkeleton class="h-7 w-22 rounded-full" />
            </div>
          </div>
        </div>
      </UCard>
    </div>

    <div
      v-else
      class="grid gap-4 xl:grid-cols-3"
    >
      <UCard variant="subtle">
        <template #header>
          <h3 class="text-base font-semibold">
            Summoner options
          </h3>
        </template>

        <ul class="grid gap-1">
          <li
            v-for="option in visibleSummonerOptions"
            :key="`${option.spell1Id}-${option.spell2Id}`"
            class="flex flex-wrap items-center justify-between gap-1"
          >
            <ChampionsChampionSummonerSpellPair
              :left="summonerPair(option).left"
              :right="summonerPair(option).right"
            />
            <ChampionsChampionOptionStats
              :games="option.games"
              :play-rate="option.playRate"
              :win-rate="option.winRate"
            />
          </li>
        </ul>
      </UCard>

      <UCard variant="subtle">
        <template #header>
          <h3 class="text-base font-semibold">
            Skill options
          </h3>
        </template>

        <ul class="grid gap-4">
          <li
            v-for="option in visibleSkillOrderOptions"
            :key="option.sequence.join('-')"
            class="flex flex-wrap items-center justify-between gap-1"
          >
            <ChampionsChampionSkillOrderDisplay
              :spells="skillSequence(option)"
            />
            <ChampionsChampionOptionStats
              :games="option.games"
              :play-rate="option.playRate"
              :win-rate="option.winRate"
            />
          </li>
        </ul>
      </UCard>

      <UCard variant="subtle">
        <template #header>
          <h3 class="text-base font-semibold">
            Starter item options
          </h3>
        </template>

        <ul class="grid gap-4">
          <li
            v-for="{ option, items } in starterItemOptions"
            :key="`starter-${option.itemIds.join('-')}`"
            class="flex flex-wrap items-center justify-between gap-1"
          >
            <div class="flex flex-wrap gap-1">
              <ChampionsChampionItemChip
                v-for="item in items"
                :key="item.id"
                :item="item"
              />
            </div>
            <ChampionsChampionOptionStats
              :games="option.games"
              :play-rate="option.playRate"
              :win-rate="option.winRate"
            />
          </li>
        </ul>
      </UCard>
    </div>
  </section>
</template>
