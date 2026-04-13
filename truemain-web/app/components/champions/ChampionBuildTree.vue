<script setup lang="ts">
import { ref, watch } from 'vue'
import type { ChampionBuildTreeNodeResponse } from '~/types/champions'
import type { StaticItemData } from '~/types/static-data'

const props = withDefaults(defineProps<{
  nodes: ChampionBuildTreeNodeResponse[]
  itemsById: Record<number, StaticItemData>
  totalGames: number
  maxChildren?: number
  minimumPickRate?: number
}>(), {
  maxChildren: 3,
  minimumPickRate: 0.05
})

const visibleNodes = computed(() =>
  props.nodes
    .filter((node) => node.pickRate >= props.minimumPickRate)
    .sort((left, right) => right.pickRate - left.pickRate)
)

const selectedRoot = ref<string>('')

const tabItems = computed(() =>
  visibleNodes.value.map((node, index) => ({
    value: `${node.itemId}-${index}`,
    label: itemsByIdLabel(node.itemId),
    badge: `${Math.round(node.pickRate * 100)}%`
  }))
)

const activeNode = computed(() => {
  if (!visibleNodes.value.length) {
    return null
  }

  const activeIndex = tabItems.value.findIndex(item => item.value === selectedRoot.value)
  return visibleNodes.value[activeIndex >= 0 ? activeIndex : 0] ?? null
})

watch(tabItems, (items) => {
  if (!items.length) {
    selectedRoot.value = ''
    return
  }

  if (!items.some(item => item.value === selectedRoot.value)) {
    selectedRoot.value = String(items[0]?.value ?? '')
  }
}, {
  immediate: true
})

function itemsByIdLabel(itemId: number): string {
  return props.itemsById[itemId]?.name ?? `Item ${itemId}`
}
</script>

<template>
  <div class="space-y-4">
    <div
      v-if="visibleNodes.length"
      class="space-y-4"
    >
      <UTabs
        v-if="tabItems.length > 1"
        v-model="selectedRoot"
        :items="tabItems"
        :content="false"
        color="neutral"
        variant="link"
        class="w-full"
      />

      <div class="overflow-x-auto pb-3">
        <div class="flex min-w-max justify-center px-2">
          <ChampionsChampionBuildTreeNode
            v-if="activeNode"
            :key="`${activeNode.itemId}-${activeNode.games}`"
            :node="activeNode"
            :item="itemsById[activeNode.itemId] ?? null"
            :items-by-id="itemsById"
            :max-children="maxChildren"
            :minimum-pick-rate="minimumPickRate"
          />
        </div>
      </div>
    </div>

    <UAlert
      v-else
      color="neutral"
      variant="subtle"
      title="Aucun nœud visible"
      description="Aucun nœud build tree ne reste après filtrage."
    />
  </div>
</template>
