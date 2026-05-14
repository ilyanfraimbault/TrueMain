<script setup lang="ts">
import { nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import type { ChampionBuildTreeNodeResponse } from '~/types/champions'
import type { StaticItemData } from '~/types/static-data'

const MAX_DEPTH = 50

const props = withDefaults(defineProps<{
  nodes: ChampionBuildTreeNodeResponse[]
  itemsById: Record<number, StaticItemData>
  correlatedBoots?: StaticItemData[]
  totalGames: number
  maxChildren?: number
  minimumPickRate?: number
}>(), {
  correlatedBoots: () => [],
  maxChildren: 3,
  minimumPickRate: 0.05
})

const visibleNodes = computed(() =>
  props.nodes
    .filter((node) => node.pickRate >= props.minimumPickRate)
    .sort((left, right) => right.pickRate - left.pickRate)
)

const normalizedMaxChildren = computed(() => props.maxChildren)
const normalizedMinimumPickRate = computed(() => props.minimumPickRate)

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

function collectGreedyPrimaryPath(
  node: ChampionBuildTreeNodeResponse,
  nodePathKey: string,
  depth: number = 0
): string[] {
  if (depth >= MAX_DEPTH) {
    return []
  }

  const primaryChild = visibleChildrenFor(node)[0]
  if (!primaryChild) {
    return []
  }

  const childPathKey = `${nodePathKey}>${primaryChild.itemId}`
  return [childPathKey, ...collectGreedyPrimaryPath(primaryChild, childPathKey, depth + 1)]
}

const primaryEdgeRanks = computed<Record<string, number>>(() => {
  if (!activeNode.value) {
    return {}
  }

  return collectGreedyPrimaryPath(
    activeNode.value,
    String(activeNode.value.itemId)
  ).reduce<Record<string, number>>((ranks, edgeKey) => {
    ranks[edgeKey] = 0
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
let resizeFrame: number | null = null

function scheduleTreeScaleUpdate() {
  if (resizeFrame !== null) {
    cancelAnimationFrame(resizeFrame)
  }

  resizeFrame = window.requestAnimationFrame(() => {
    resizeFrame = null
    void updateTreeScale()
  })
}

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
  scheduleTreeScaleUpdate()
})

onMounted(() => {
  scheduleTreeScaleUpdate()

  if (typeof ResizeObserver !== 'undefined') {
    resizeObserver = new ResizeObserver(() => {
      scheduleTreeScaleUpdate()
    })

    if (treeContainerRef.value) {
      resizeObserver.observe(treeContainerRef.value)
    }

    if (treeContentRef.value) {
      resizeObserver.observe(treeContentRef.value)
    }
  }
  else {
    window.addEventListener('resize', scheduleTreeScaleUpdate)
  }
})

onBeforeUnmount(() => {
  if (resizeFrame !== null) {
    cancelAnimationFrame(resizeFrame)
  }

  resizeObserver?.disconnect()
  window.removeEventListener('resize', scheduleTreeScaleUpdate)
})
</script>

<template>
  <div class="space-y-4">
    <div
      v-if="visibleNodes.length"
      class="space-y-4"
    >
      <div class="flex flex-wrap items-start justify-between gap-3">
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
          v-if="correlatedBoots.length"
          class="ml-auto flex flex-wrap items-center justify-end gap-2"
        >
          <span class="text-sm text-muted">Correlated boots</span>
          <ChampionsChampionItemChip
            v-for="item in correlatedBoots"
            :key="`tree-boots-${item.id}`"
            :item="item"
          />
        </div>
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
              :max-children="normalizedMaxChildren"
              :minimum-pick-rate="normalizedMinimumPickRate"
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
