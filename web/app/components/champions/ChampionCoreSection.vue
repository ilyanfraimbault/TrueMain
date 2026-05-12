<script setup lang="ts">
import type { ChampionCoreResponse } from '~/types/champions'
import type { ChampionStaticData } from '~/types/static-data'
import {
  getStaticSummonerSpell,
  mapItemIdsToStaticItems,
  mapItemSetToStaticItems,
  mapSkillSequenceToStaticSpells,
  sortItemsByGoldDesc
} from '~/utils/champion-display'

const props = defineProps<{
  core: ChampionCoreResponse
  championStatic: ChampionStaticData
  isStaticPending: boolean
}>()

const starterItems = computed(() =>
  sortItemsByGoldDesc(mapItemSetToStaticItems(props.core.starterItems, props.championStatic.items)))

const buildPathItems = computed(() =>
  mapItemIdsToStaticItems(props.core.buildPath?.itemIds ?? [], props.championStatic.items))

const bootsItems = computed(() =>
  sortItemsByGoldDesc(mapItemSetToStaticItems(props.core.boots, props.championStatic.items)))

const summonerLeft = computed(() =>
  props.core.summonerSpells
    ? getStaticSummonerSpell(props.championStatic.summonerSpells, props.core.summonerSpells.spell1Id)
    : null)

const summonerRight = computed(() =>
  props.core.summonerSpells
    ? getStaticSummonerSpell(props.championStatic.summonerSpells, props.core.summonerSpells.spell2Id)
    : null)

const skillSequence = computed(() =>
  props.core.skillOrder
    ? mapSkillSequenceToStaticSpells(props.core.skillOrder.sequence, props.championStatic.championSpells)
    : [])
</script>

<template>
  <section class="space-y-4">
    <h2 class="text-xl font-semibold">
      Core
    </h2>

    <UCard variant="subtle">
      <div class="grid gap-4 md:grid-cols-2 xl:grid-cols-7">
        <div class="space-y-3">
          <p class="text-sm font-medium text-muted">
            Starter items
          </p>
          <div
            v-if="isStaticPending"
            class="flex items-center gap-1"
          >
            <USkeleton
              v-for="item in 3"
              :key="item"
              class="size-14 rounded-xl"
            />
          </div>
          <div
            v-else
            class="flex flex-wrap gap-1"
          >
            <ChampionsChampionItemChip
              v-for="item in starterItems"
              :key="`starter-${item.id}`"
              :item="item"
            />
          </div>
        </div>

        <div class="space-y-3">
          <p class="text-sm font-medium text-muted">
            Build path
          </p>
          <div
            v-if="isStaticPending"
            class="flex items-center gap-1"
          >
            <USkeleton
              v-for="item in 3"
              :key="item"
              class="size-14 rounded-xl"
            />
          </div>
          <div
            v-else
            class="flex flex-wrap gap-1"
          >
            <ChampionsChampionItemChip
              v-for="item in buildPathItems"
              :key="item.id"
              :item="item"
            />
          </div>
        </div>

        <div class="space-y-3">
          <p class="text-sm font-medium text-muted">
            Boots
          </p>
          <div
            v-if="isStaticPending"
            class="flex flex-wrap gap-1"
          >
            <USkeleton class="size-14 rounded-xl" />
          </div>
          <div
            v-else
            class="flex flex-wrap gap-1"
          >
            <ChampionsChampionItemChip
              v-for="item in bootsItems"
              :key="`boots-${item.id}`"
              :item="item"
            />
          </div>
        </div>

        <div class="space-y-3">
          <p class="text-sm font-medium text-muted">
            Summoners
          </p>
          <div
            v-if="isStaticPending"
            class="flex items-center gap-1"
          >
            <USkeleton class="size-14 rounded-xl" />
            <USkeleton class="size-14 rounded-xl" />
          </div>
          <ChampionsChampionSummonerSpellPair
            v-else
            :left="summonerLeft"
            :right="summonerRight"
          />
        </div>

        <div class="space-y-3">
          <p class="text-sm font-medium text-muted">
            Skill order
          </p>
          <div
            v-if="isStaticPending"
            class="flex items-center gap-1"
          >
            <USkeleton class="size-14 rounded-xl" />
            <USkeleton class="size-14 rounded-xl" />
            <USkeleton class="size-14 rounded-xl" />
          </div>
          <ChampionsChampionSkillOrderDisplay
            v-else
            :spells="skillSequence"
          />
        </div>

        <div class="space-y-3 md:col-span-2 xl:col-span-2">
          <p class="text-sm font-medium text-muted">
            Runes
          </p>
          <div
            v-if="isStaticPending"
            class="flex flex-col gap-2"
          >
            <USkeleton class="h-12 w-40 rounded-xl" />
            <USkeleton class="h-9 w-32 rounded-xl" />
            <USkeleton class="h-6 w-28 rounded-xl" />
          </div>
          <ChampionsChampionRunePageDisplay
            v-else-if="core.runePage"
            :page="core.runePage"
            :perks="championStatic.perks"
            :perk-styles="championStatic.perkStyles"
          />
          <p
            v-else
            class="text-xs text-muted"
          >
            No data
          </p>
        </div>
      </div>
    </UCard>
  </section>
</template>
