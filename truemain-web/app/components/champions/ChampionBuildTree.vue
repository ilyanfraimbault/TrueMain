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

const rootItems = computed(() =>
  visibleNodes.value.map((node, index) => ({
    value: `${node.itemId}-${index}`,
    itemId: node.itemId,
    item: props.itemsById[node.itemId] ?? null,
    badge: `${Math.round(node.pickRate * 100)}%`
  }))
)

const activeNode = computed(() => {
  if (!visibleNodes.value.length) {
    return null
  }

  const activeIndex = rootItems.value.findIndex(item => item.value === selectedRoot.value)
  return visibleNodes.value[activeIndex >= 0 ? activeIndex : 0] ?? null
})

watch(rootItems, (items) => {
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
</script>

<template>
  <div class="space-y-4">
    <div
      v-if="visibleNodes.length"
      class="space-y-4"
    >
      <div
        v-if="rootItems.length > 1"
        class="flex flex-wrap items-center gap-3"
      >
        <UButton
          v-for="root in rootItems"
          :key="root.value"
          type="button"
          color="neutral"
          :variant="selectedRoot === root.value ? 'soft' : 'ghost'"
          :title="root.item?.name ?? `Item ${root.itemId}`"
          class="rounded-xl px-2 py-1.5"
          @click="selectedRoot = root.value"
        >
          <div class="flex items-center gap-2">
            <ChampionsChampionAsyncImage
              v-if="root.item"
              :src="root.item.iconUrl"
              :alt="root.item.name"
              size-class="size-8"
              image-class="rounded-md border border-default bg-default object-cover"
              wrapper-class="rounded-md"
              width="32"
              height="32"
            />
            <div
              v-else
              class="size-8 rounded-md border border-default bg-elevated"
            />
            <UBadge
              color="neutral"
              variant="subtle"
              size="sm"
            >
              {{ root.badge }}
            </UBadge>
          </div>
        </UButton>
      </div>

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
