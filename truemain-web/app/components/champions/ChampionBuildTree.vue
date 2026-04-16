<script setup lang="ts">
import { nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
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
const treeContainerRef = ref<HTMLElement | null>(null)
const treeContentRef = ref<HTMLElement | null>(null)
const treeScale = ref(1)
const scaledTreeHeight = ref<number | null>(null)

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

function visibleChildrenFor(node: ChampionBuildTreeNodeResponse) {
  return [...node.children]
    .filter((child) => child.pickRate >= props.minimumPickRate)
    .sort((left, right) => right.pickRate - left.pickRate)
    .slice(0, props.maxChildren)
}

function collectPathSummaries(
  node: ChampionBuildTreeNodeResponse,
  cumulativePickRate: number,
  nodePathKey: string
): Array<{ score: number, edgeKeys: string[] }> {
  const children = visibleChildrenFor(node)

  if (!children.length) {
    return [{ score: cumulativePickRate, edgeKeys: [] }]
  }

  return children.flatMap((child) => {
    const childPathKey = `${nodePathKey}>${child.itemId}`
    const childCumulativePickRate = cumulativePickRate * child.pickRate

    return collectPathSummaries(child, childCumulativePickRate, childPathKey).map(path => ({
      score: path.score,
      edgeKeys: [childPathKey, ...path.edgeKeys]
    }))
  })
}

const primaryEdgeRanks = computed<Record<string, number>>(() => {
  if (!activeNode.value) {
    return {}
  }

  const topPaths = collectPathSummaries(
    activeNode.value,
    activeNode.value.pickRate,
    String(activeNode.value.itemId)
  )
    .sort((left, right) => right.score - left.score || right.edgeKeys.length - left.edgeKeys.length)
    .slice(0, 2)

  return topPaths.reduce<Record<string, number>>((ranks, path, index) => {
    for (const edgeKey of path.edgeKeys) {
      const currentRank = ranks[edgeKey]
      if (currentRank === undefined || index < currentRank) {
        ranks[edgeKey] = index
      }
    }

    return ranks
  }, {})
})

async function updateTreeScale() {
  await nextTick()

  const container = treeContainerRef.value
  const content = treeContentRef.value

  if (!container || !content) {
    treeScale.value = 1
    scaledTreeHeight.value = null
    return
  }

  const availableWidth = Math.max(container.clientWidth - 16, 0)
  const contentWidth = content.scrollWidth
  const contentHeight = content.scrollHeight

  if (availableWidth <= 0 || contentWidth <= 0 || contentHeight <= 0) {
    treeScale.value = 1
    scaledTreeHeight.value = null
    return
  }

  const nextScale = Math.min(1, availableWidth / contentWidth)
  treeScale.value = nextScale
  scaledTreeHeight.value = contentHeight * nextScale
}

let resizeObserver: ResizeObserver | null = null

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

watch(activeNode, () => {
  void updateTreeScale()
})

onMounted(() => {
  void updateTreeScale()

  if (typeof ResizeObserver !== 'undefined') {
    resizeObserver = new ResizeObserver(() => {
      void updateTreeScale()
    })

    if (treeContainerRef.value) {
      resizeObserver.observe(treeContainerRef.value)
    }

    if (treeContentRef.value) {
      resizeObserver.observe(treeContentRef.value)
    }
  }

  window.addEventListener('resize', updateTreeScale)
})

onBeforeUnmount(() => {
  resizeObserver?.disconnect()
  window.removeEventListener('resize', updateTreeScale)
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

      <div
        ref="treeContainerRef"
        class="overflow-hidden pb-3"
      >
        <div
          class="flex justify-center px-2"
          :style="scaledTreeHeight ? { height: `${scaledTreeHeight}px` } : undefined"
        >
          <div
            ref="treeContentRef"
            class="origin-top"
            :style="{ transform: `scale(${treeScale})` }"
          >
            <ChampionsChampionBuildTreeNode
              v-if="activeNode"
              :key="`${activeNode.itemId}-${activeNode.games}`"
              :node="activeNode"
              :item="itemsById[activeNode.itemId] ?? null"
              :items-by-id="itemsById"
              :max-children="maxChildren"
              :minimum-pick-rate="minimumPickRate"
              :cumulative-pick-rate="activeNode.pickRate"
              :node-path-key="String(activeNode.itemId)"
              :primary-edge-ranks="primaryEdgeRanks"
            />
          </div>
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
