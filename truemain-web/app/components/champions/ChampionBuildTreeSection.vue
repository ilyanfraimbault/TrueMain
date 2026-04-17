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
      <div class="space-y-1">
        <h2 class="text-lg font-semibold">
          Build tree
        </h2>
        <p class="text-sm text-muted">
          Arbre des probabilités d’achat.
        </p>
      </div>
    </template>

    <div class="space-y-4">
      <div
        v-if="isStaticPending"
        class="space-y-4"
      >
        <div class="flex flex-wrap items-start justify-between gap-3">
          <div class="flex flex-wrap items-center gap-3">
            <USkeleton
              v-for="index in 4"
              :key="`tree-root-skeleton-${index}`"
              class="size-10 rounded-md"
            />
          </div>

          <div class="ml-auto flex items-center gap-2">
            <span class="text-sm text-muted">Correlated boots</span>
            <USkeleton class="size-10 rounded-md" />
          </div>
        </div>

        <div class="flex justify-center py-8">
          <div class="flex min-w-max flex-col items-center gap-4">
            <USkeleton class="size-14 rounded-xl" />
            <USkeleton class="h-4 w-px" />
            <div class="flex items-start gap-8">
              <div class="flex flex-col items-center gap-4">
                <USkeleton class="h-px w-16" />
                <USkeleton class="size-14 rounded-xl" />
              </div>
              <div class="flex flex-col items-center gap-4">
                <USkeleton class="h-px w-16" />
                <USkeleton class="size-14 rounded-xl" />
                <USkeleton class="h-4 w-px" />
                <div class="flex items-center gap-3">
                  <USkeleton class="size-14 rounded-xl" />
                  <USkeleton class="size-14 rounded-xl" />
                  <USkeleton class="size-14 rounded-xl" />
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>

      <ChampionsChampionBuildTree
        v-else
        :nodes="buildTree.build"
        :items-by-id="championStatic.items"
        :correlated-boots="correlatedBoots"
        :total-games="buildTree.totalGames"
        :max-children="3"
        :minimum-pick-rate="0.05"
      />
    </div>
  </UCard>
</template>
