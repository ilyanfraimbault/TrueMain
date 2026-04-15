<script setup lang="ts">
import type { ChampionBuildTreeResponse, ItemSetOptionResponse } from '~/types/champions'
import type { ChampionStaticData } from '~/types/static-data'
import { mapItemSetToStaticItems, sortItemsByGoldDesc } from '~/utils/champion-display'

const props = defineProps<{
  buildTree: ChampionBuildTreeResponse
  championStatic: ChampionStaticData
  boots: ItemSetOptionResponse | null
  isStaticPending: boolean
}>()

const correlatedBoots = computed(() =>
  sortItemsByGoldDesc(mapItemSetToStaticItems(props.boots, props.championStatic.items)))
</script>

<template>
  <UCard variant="subtle">
    <template #header>
      <div class="flex flex-col gap-3 md:flex-row md:items-start md:justify-between">
        <div class="space-y-1">
          <h2 class="text-lg font-semibold">
            Build tree
          </h2>
          <p class="text-sm text-muted">
            Arbre des probabilités d’achat.
          </p>
        </div>
        <div
          v-if="!isStaticPending && correlatedBoots.length > 0"
          class="flex items-center gap-2"
        >
          <span class="text-sm text-muted">Correlated boots</span>
          <ChampionsChampionItemChip
            v-for="item in correlatedBoots"
            :key="`tree-boots-${item.id}`"
            :item="item"
          />
        </div>
      </div>
    </template>

    <ChampionsChampionBuildTree
      :nodes="buildTree.build"
      :items-by-id="championStatic.items"
      :total-games="buildTree.totalGames"
      :max-children="3"
      :minimum-pick-rate="0.05"
    />
  </UCard>
</template>
