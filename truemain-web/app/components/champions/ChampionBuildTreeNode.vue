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
  nodePathKey: string
  primaryEdgeRanks: Record<string, number>
}>()

const visibleChildren = computed(() => {
  return [...props.node.children]
    .filter((child) => child.pickRate >= props.minimumPickRate)
    .sort((left, right) => right.pickRate - left.pickRate)
    .slice(0, props.maxChildren)
})

const currentPathRank = computed(() => {
  const ownRank = props.primaryEdgeRanks[props.nodePathKey]
  if (ownRank !== undefined) {
    return ownRank
  }

  const childRanks = branchSegments.value
    .map(segment => props.primaryEdgeRanks[segment.childPathKey])
    .filter((rank): rank is number => rank !== undefined)

  if (!childRanks.length) {
    return undefined
  }

  return Math.min(...childRanks)
})

const primaryStemStyle = computed(() => {
  const currentStroke = getBranchStroke(currentPathRank.value)
  const stemStroke = branchSegments.value.length === 1
    ? branchSegments.value[0]!.stroke
    : currentStroke

  return {
    '--branch-thickness': `${stemStroke.strokeWidth}px`,
    '--branch-background': stemStroke.background
  }
})

const branchGuideHeight = 14
const stemHeight = 14
const childCardGap = 6
const childrenRowRef = ref<HTMLElement | null>(null)
const childRefs = ref<HTMLElement[]>([])
const branchSegments = ref<Array<{
  childX: number
  child: ChampionBuildTreeNodeResponse
  childPathKey: string
  stroke: ReturnType<typeof getBranchStroke>
}>>([])
const branchRowWidth = ref(0)
const parentCenterX = ref(0)

function setChildRef(element: Element | ComponentPublicInstance | null, index: number) {
  if (!(element instanceof HTMLElement)) {
    return
  }

  childRefs.value[index] = element
}

const tooltipText = computed(() =>
  `${props.item?.name ?? 'Unknown item'} ${props.item?.id ?? props.node.itemId} • ${formatPercentage(props.node.pickRate)} pick rate • ${props.node.games} games • ${formatPercentage(props.node.wins / Math.max(props.node.games, 1))} WR`)

function getBranchStroke(pathRank?: number) {
  if (pathRank === undefined) {
    return {
      strokeWidth: 1.5,
      strokeDasharray: '4 3',
      strokeOpacity: 1,
      background: 'repeating-linear-gradient(to bottom, rgb(71 85 105) 0 7px, transparent 7px 12px)'
    }
  }

  return {
    strokeWidth: 2.2,
    strokeDasharray: undefined,
    strokeOpacity: 1,
    background: 'rgb(71 85 105)'
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
    .map((child, index) => {
      const childElement = childRefs.value[index]
      if (!childElement) {
        return null
      }

      const childCard = childElement.querySelector<HTMLElement>('.tree-node__card')
      const childRect = (childCard ?? childElement).getBoundingClientRect()
      const childPathKey = `${props.nodePathKey}>${child.itemId}`
      const childPathRank = props.primaryEdgeRanks[childPathKey]

      return {
        childX: childRect.left - rowRect.left + (childRect.width / 2),
        child,
        childPathKey,
        stroke: getBranchStroke(childPathRank)
      }
    })
    .filter((segment): segment is {
      childX: number
      child: ChampionBuildTreeNodeResponse
      childPathKey: string
      stroke: ReturnType<typeof getBranchStroke>
    } => segment !== null)

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

const hasSingleVisibleChild = computed(() => visibleChildren.value.length === 1)
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
        v-if="!hasSingleVisibleChild"
        class="tree-node__stem"
        :style="parentOffsetStyle"
      />

      <div
        ref="childrenRowRef"
        class="tree-node__children-row"
        :style="{
          '--branch-guide-height': `${branchGuideHeight}px`,
          '--stem-height': `${stemHeight}px`
        }"
      >
        <svg
          v-if="branchRowWidth > 0"
          class="tree-node__branches"
          :viewBox="`0 ${hasSingleVisibleChild ? -stemHeight : 0} ${branchRowWidth} ${branchGuideHeight + (hasSingleVisibleChild ? stemHeight : 0)}`"
          preserveAspectRatio="none"
          aria-hidden="true"
        >
          <g
            v-for="segment in branchSegments"
            :key="`${node.itemId}-${segment.child.itemId}-${segment.childX}`"
          >
            <path
              :d="hasSingleVisibleChild
                ? `M ${parentCenterX} ${-stemHeight} V 0 H ${segment.childX} V ${branchGuideHeight - childCardGap}`
                : `M ${parentCenterX} 0 H ${segment.childX} V ${branchGuideHeight - childCardGap}`"
              stroke="rgb(71 85 105)"
              fill="none"
              stroke-linecap="square"
              stroke-linejoin="miter"
              :stroke-width="segment.stroke.strokeWidth"
              :stroke-opacity="segment.stroke.strokeOpacity"
              :stroke-dasharray="segment.stroke.strokeDasharray"
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
            :node-path-key="`${nodePathKey}>${child.itemId}`"
            :primary-edge-ranks="primaryEdgeRanks"
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
  position: relative;
  z-index: 1;
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
  border-radius: 0;
  background: var(--branch-background, rgb(71 85 105));
}

.tree-node__children-row {
  position: relative;
  display: inline-flex;
  gap: 0.9rem;
  align-items: flex-start;
  padding-top: var(--branch-guide-height, 14px);
}

.tree-node__child {
  position: relative;
  z-index: 1;
  display: grid;
  justify-items: center;
}

.tree-node__branches {
  position: absolute;
  top: 0;
  left: 0;
  z-index: 0;
  width: 100%;
  height: var(--branch-guide-height, 14px);
  overflow: visible;
  pointer-events: none;
}
</style>
