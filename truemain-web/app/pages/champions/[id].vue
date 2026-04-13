<script setup lang="ts">
import type { ItemSetOptionResponse } from '~/types/champions'
import type {
  StaticChampionSpellData,
  StaticItemData,
  StaticSummonerSpellData
} from '~/types/static-data'
import { formatPercentage } from '~/utils/items'

const championId = computed(() => Number.parseInt(String(useRoute().params.id), 10))

const {
  advanced,
  buildTree,
  champion,
  championState,
  championStatic,
  core,
  effectivePosition,
  filters,
  isPageLoading,
  isStaticPending,
  patchOptions,
  positionOptions,
  setPositionFilter,
  summary
} = useChampionPageStore(championId)

function getSummonerSpell(id: number): StaticSummonerSpellData | null {
  return championStatic.value.summonerSpells[id] ?? null
}

function getChampionSpell(sequenceKey: string): StaticChampionSpellData | null {
  return championStatic.value.championSpells[sequenceKey] ?? null
}

function getSkillSequence(sequence: string[]): StaticChampionSpellData[] {
  return sequence
    .map(getChampionSpell)
    .filter((spell): spell is StaticChampionSpellData => spell !== null)
}

function getItem(itemId: number): StaticItemData | null {
  return championStatic.value.items[itemId] ?? null
}

function getItemSet(option: ItemSetOptionResponse | null): StaticItemData[] {
  if (!option) {
    return []
  }

  return option.itemIds
    .map(getItem)
    .filter((item): item is StaticItemData => item !== null)
}

const starterItemIds = computed(() =>
  new Set(
    (advanced.value?.starterItemOptions ?? [])
      .flatMap(option => option.itemIds)
      .filter(itemId => itemId > 0)
  ))

const visibleBuildTreeNodes = computed(() =>
  (buildTree.value?.build ?? []).filter(node => !starterItemIds.value.has(node.itemId)))

const sortedCoreStarterItems = computed(() =>
  getItemSet(core.value?.starterItems ?? null)
    .sort((left, right) => right.totalGold - left.totalGold || left.id - right.id))

const primaryBuildPath = computed(() =>
  (core.value?.buildPath?.itemIds ?? [])
    .map(getItem)
    .filter((item): item is StaticItemData => item !== null))

const visibleSummonerOptions = computed(() =>
  (advanced.value?.summonerSpellOptions ?? []).filter((option) => option.playRate >= 0.1))

const visibleSkillOrderOptions = computed(() =>
  (advanced.value?.skillOrderOptions ?? []).filter((option) => option.playRate >= 0.1))

const sortedStarterItemOptions = computed(() =>
  (advanced.value?.starterItemOptions ?? [])
    .filter((option) => option.playRate >= 0.1)
    .map((option) => ({
      option,
      items: getItemSet(option).sort((left, right) => right.totalGold - left.totalGold || left.id - right.id)
    })))

useSeoMeta({
  title: championStatic.value.championName || 'TrueMain',
  description: `Vue champion et build tree pour le champion ${championId.value}.`
})
</script>

<template>
  <main class="mx-auto max-w-5xl space-y-8 p-4 md:p-6">
    <ChampionsChampionPageSkeleton v-if="isPageLoading" />

    <section v-else-if="championState.error.value">
      <UAlert
        color="error"
        variant="soft"
        title="Impossible de charger /champions/{id}"
        :description="championState.error.value.message"
      />
    </section>

    <section
      v-else-if="champion && summary && core && advanced && buildTree"
      class="space-y-8"
    >
      <header class="flex flex-col gap-6 md:flex-row md:items-start md:justify-between">
        <div class="flex items-center gap-5">
          <img
            v-if="championStatic.championIconUrl"
            :src="championStatic.championIconUrl"
            :alt="championStatic.championName ?? ''"
            class="size-28 shrink-0 rounded-3xl border border-default object-cover shadow-sm"
            loading="lazy"
          >
          <div
            v-else
            class="size-28 shrink-0 rounded-3xl border border-default bg-elevated"
          />

          <div class="space-y-3">
            <div class="space-y-2">
              <h1
                v-if="championStatic.championName"
                class="text-3xl font-semibold tracking-tight"
              >
                {{ championStatic.championName }}
              </h1>
            </div>

            <dl class="flex flex-wrap items-end gap-x-10 gap-y-3 text-sm">
              <div>
                <dt class="text-muted">
                  Games
                </dt>
                <dd class="mt-1 text-2xl font-semibold tracking-tight">
                  {{ summary.games }}
                </dd>
              </div>
              <div>
                <dt class="text-muted">
                  Win rate
                </dt>
                <dd class="mt-1 text-2xl font-semibold tracking-tight">
                  {{ formatPercentage(summary.winRate) }}
                </dd>
              </div>
            </dl>
          </div>
        </div>

        <div class="ml-auto flex w-full max-w-md flex-col items-end gap-4">
          <UFormField
            label="Patch"
            class="w-36"
          >
            <USelect
              v-model="filters.patch"
              :items="patchOptions"
              color="neutral"
              variant="subtle"
              class="w-36"
            />
          </UFormField>

          <div class="flex w-full justify-end">
            <UFieldGroup
              size="md"
              class="rounded-xl bg-elevated/40 p-1"
            >
              <UButton
                v-for="option in positionOptions"
                :key="option.value"
                type="button"
                color="neutral"
                square
                :variant="effectivePosition === option.value ? 'soft' : 'ghost'"
                :title="option.label"
                :aria-label="option.label"
                @click="setPositionFilter(option.value)"
              >
                <img
                  :src="option.iconUrl"
                  :alt="option.label"
                  class="size-5 object-contain"
                  loading="lazy"
                >
              </UButton>
            </UFieldGroup>
          </div>
        </div>
      </header>

      <section class="space-y-4">
        <h2 class="text-xl font-semibold">
          Core
        </h2>

        <UCard variant="subtle">
          <div class="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
            <div class="space-y-3">
              <p class="text-sm font-medium text-muted">
                Starter items
              </p>
              <div
                v-if="isStaticPending"
                class="flex flex-wrap gap-1"
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
                  v-for="item in sortedCoreStarterItems"
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
                class="flex flex-wrap gap-1"
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
                  v-for="item in primaryBuildPath"
                  :key="item.id"
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
                class="flex gap-1"
              >
                <USkeleton class="size-14 rounded-xl" />
                <USkeleton class="size-14 rounded-xl" />
              </div>
              <ChampionsChampionSummonerSpellPair
                v-else
                :left="core.summonerSpells ? getSummonerSpell(core.summonerSpells.spell1Id) : null"
                :right="core.summonerSpells ? getSummonerSpell(core.summonerSpells.spell2Id) : null"
              />
            </div>

            <div class="space-y-3">
              <p class="text-sm font-medium text-muted">
                Skill order
              </p>
              <div
                v-if="isStaticPending"
                class="flex gap-1"
              >
                <USkeleton class="size-14 rounded-xl" />
                <USkeleton class="size-14 rounded-xl" />
                <USkeleton class="size-14 rounded-xl" />
              </div>
              <ChampionsChampionSkillOrderDisplay
                v-else
                :spells="core.skillOrder ? getSkillSequence(core.skillOrder.sequence) : []"
              />
            </div>
          </div>
        </UCard>
      </section>

      <section class="space-y-4">
        <h2 class="text-xl font-semibold">
          Advanced details
        </h2>

        <div class="grid gap-4 xl:grid-cols-3">
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
                  :left="getSummonerSpell(option.spell1Id)"
                  :right="getSummonerSpell(option.spell2Id)"
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
                  :spells="getSkillSequence(option.sequence)"
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
                v-for="{ option, items } in sortedStarterItemOptions"
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

      <UCard variant="subtle">
        <template #header>
          <div class="space-y-1">
            <h2 class="text-lg font-semibold">
              Build tree
            </h2>
            <p class="text-sm text-muted">
              Arbre des probabilités d’achat.
            </p>
          </div>
        </template>

        <ChampionsChampionBuildTree
          :nodes="visibleBuildTreeNodes"
          :items-by-id="championStatic.items"
          :total-games="buildTree.totalGames"
          :max-children="3"
          :minimum-pick-rate="0.05"
        />
      </UCard>
    </section>
  </main>
</template>
