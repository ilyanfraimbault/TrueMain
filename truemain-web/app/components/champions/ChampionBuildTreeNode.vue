<script setup lang="ts">
import type { ChampionBuildTreeNodeResponse } from '~/types/champions'
import type { StaticItemData } from '~/types/static-data'
import { formatPercentage } from '~/utils/items'
import type { ComponentPublicInstance } from 'vue'
import { nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'

defineOptions({
  name: 'ChampionBuildTreeNode'
})

const props = defineProps<{
  node: ChampionBuildTreeNodeResponse
  item: StaticItemData | null
  itemsById: Record<number, StaticItemData>
  maxChildren: number
  minimumPickRate: number
}>()

const visibleChildren = computed(() => {
  return [...props.node.children]
    .filter((child) => child.pickRate >= props.minimumPickRate)
    .sort((left, right) => right.pickRate - left.pickRate)
    .slice(0, props.maxChildren)
})

const primaryStemStyle = computed(() => {
  const primaryStroke = getBranchStroke(0)

  return {
    '--branch-opacity': String(primaryStroke.strokeOpacity),
    '--branch-thickness': `${primaryStroke.strokeWidth}px`
  }
})

const branchGuideHeight = 14
const childrenRowRef = ref<HTMLElement | null>(null)
const childRefs = ref<HTMLElement[]>([])
const branchSegments = ref<Array<{ childX: number, rank: number }>>([])
const branchRowWidth = ref(0)
const parentCenterX = ref(0)

function setChildRef(element: Element | ComponentPublicInstance | null, index: number) {
  if (!(element instanceof HTMLElement)) {
    return
  }

  childRefs.value[index] = element
}

const tooltipText = computed(() =>
  `${props.item?.name ?? 'Unknown item'} • ${formatPercentage(props.node.pickRate)} pick rate • ${props.node.games} games • ${formatPercentage(props.node.wins / Math.max(props.node.games, 1))} WR`)

function getBranchStroke(rank: number) {
  if (rank === 0) {
    return {
      strokeWidth: 2.5,
      strokeDasharray: undefined,
      strokeOpacity: 0.92
    }
  }

  if (rank === 1) {
    return {
      strokeWidth: 1.75,
      strokeDasharray: undefined,
      strokeOpacity: 0.72
    }
  }

  return {
    strokeWidth: 1.5,
    strokeDasharray: '4 4',
    strokeOpacity: 0.56
  }
}

async function updateBranchSegments() {
  await nextTick()

  const row = childrenRowRef.value
  if (!row) {
    branchSegments.value = []
    return
  }

  const rowRect = row.getBoundingClientRect()
  branchRowWidth.value = rowRect.width

  branchSegments.value = visibleChildren.value
    .map((_, index) => {
      const child = childRefs.value[index]
      if (!child) {
        return null
      }

      const childCard = child.querySelector<HTMLElement>('.tree-node__card')
      const childRect = (childCard ?? child).getBoundingClientRect()

      return {
        childX: childRect.left - rowRect.left + (childRect.width / 2),
        rank: index
      }
    })
    .filter((segment): segment is { childX: number, rank: number } => segment !== null)

  if (!branchSegments.value.length) {
    parentCenterX.value = rowRect.width / 2
    return
  }

  const leftMostChildX = Math.min(...branchSegments.value.map(segment => segment.childX))
  const rightMostChildX = Math.max(...branchSegments.value.map(segment => segment.childX))
  parentCenterX.value = (leftMostChildX + rightMostChildX) / 2
}

let resizeObserver: ResizeObserver | null = null

watch(visibleChildren, () => {
  childRefs.value = []
  void updateBranchSegments()
}, {
  deep: true,
  immediate: true
})

onMounted(() => {
  void updateBranchSegments()

  if (typeof ResizeObserver !== 'undefined' && childrenRowRef.value) {
    resizeObserver = new ResizeObserver(() => {
      void updateBranchSegments()
    })

    resizeObserver.observe(childrenRowRef.value)
  }

  window.addEventListener('resize', updateBranchSegments)
})

onBeforeUnmount(() => {
  resizeObserver?.disconnect()
  window.removeEventListener('resize', updateBranchSegments)
})

const parentOffsetStyle = computed(() => {
  if (branchRowWidth.value <= 0) {
    return undefined
  }

  return {
    transform: `translateX(${parentCenterX.value - (branchRowWidth.value / 2)}px)`
  }
})
</script>

<template>
  <div class="tree-node">
    <UTooltip
      :text="tooltipText"
      :content="{ side: 'top' }"
      arrow
    >
      <div
        class="tree-node__card"
        :style="parentOffsetStyle"
      >
        <ChampionsChampionAsyncImage
          v-if="item"
          :src="item.iconUrl"
          :alt="item.name"
          wrapper-class="tree-node__image"
          loading="lazy"
        />
        <div
          v-else
          class="tree-node__fallback"
        />
      </div>
    </UTooltip>

    <div
      v-if="visibleChildren.length"
      class="tree-node__children"
      :style="primaryStemStyle"
    >
      <div
        class="tree-node__stem"
        :style="parentOffsetStyle"
      />

      <div
        ref="childrenRowRef"
        class="tree-node__children-row"
        :style="{ '--branch-guide-height': `${branchGuideHeight}px` }"
      >
        <svg
          v-if="branchRowWidth > 0"
          class="tree-node__branches"
          :viewBox="`0 0 ${branchRowWidth} ${branchGuideHeight}`"
          preserveAspectRatio="none"
          aria-hidden="true"
        >
          <g
            v-for="segment in branchSegments"
            :key="`${node.itemId}-${segment.rank}-${segment.childX}`"
          >
            <line
              :x1="parentCenterX"
              y1="0"
              :x2="segment.childX"
              y2="0"
              stroke="rgb(71 85 105)"
              stroke-linecap="round"
              :stroke-width="getBranchStroke(segment.rank).strokeWidth"
              :stroke-opacity="getBranchStroke(segment.rank).strokeOpacity"
              :stroke-dasharray="getBranchStroke(segment.rank).strokeDasharray"
            />
            <line
              :x1="segment.childX"
              y1="0"
              :x2="segment.childX"
              :y2="branchGuideHeight"
              stroke="rgb(71 85 105)"
              stroke-linecap="round"
              :stroke-width="getBranchStroke(segment.rank).strokeWidth"
              :stroke-opacity="getBranchStroke(segment.rank).strokeOpacity"
              :stroke-dasharray="getBranchStroke(segment.rank).strokeDasharray"
            />
          </g>
        </svg>

        <div
          v-for="(child, index) in visibleChildren"
          :key="`${node.itemId}-${child.itemId}-${child.games}`"
          class="tree-node__child"
          :ref="(element) => setChildRef(element, index)"
        >
          <ChampionsChampionBuildTreeNode
            :node="child"
            :item="itemsById[child.itemId] ?? null"
            :items-by-id="itemsById"
            :max-children="maxChildren"
            :minimum-pick-rate="minimumPickRate"
          />
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.tree-node {
  --tree-card-size: 3rem;
  --tree-card-padding: 0.35rem;
  --tree-card-outer-size: calc(var(--tree-card-size) + (var(--tree-card-padding) * 2));
  display: inline-flex;
  flex-direction: column;
  align-items: center;
  min-width: max-content;
}

.tree-node__card {
  display: grid;
  place-items: center;
  padding: var(--tree-card-padding);
  border: 1px solid rgba(148, 163, 184, 0.22);
  border-radius: 0.9rem;
  background: var(--ui-bg);
}

.tree-node__image,
.tree-node__fallback {
  width: var(--tree-card-size);
  height: var(--tree-card-size);
  border-radius: 0.7rem;
  object-fit: cover;
}

.tree-node__fallback {
  background: rgba(148, 163, 184, 0.18);
}

.tree-node__children {
  display: grid;
  justify-items: center;
  width: max-content;
  min-width: 100%;
}

.tree-node__stem {
  width: var(--branch-thickness, 1px);
  height: 0.85rem;
  border-radius: 999px;
  background: rgba(71, 85, 105, var(--branch-opacity, 0.5));
}

.tree-node__children-row {
  position: relative;
  display: inline-flex;
  gap: 0.9rem;
  align-items: flex-start;
  padding-top: var(--branch-guide-height, 14px);
}

.tree-node__child {
  display: grid;
  justify-items: center;
}

.tree-node__branches {
  position: absolute;
  top: 0;
  left: 0;
  width: 100%;
  height: var(--branch-guide-height, 14px);
  overflow: visible;
  pointer-events: none;
}
</style>
