<script setup lang="ts">
import type { BuildTreeNode } from '~~/shared/types/champions'
import type { StaticItemData } from '~~/shared/types/static-data'

const props = defineProps<{
  tree: BuildTreeNode[]
  firstItemId: number
  /** The backend's core "Build path" (itemIds, starting at firstItemId). The
   * solid main-edge highlight traces this exactly, so it stops where the core
   * path stops rather than running to the deepest leaf. */
  itemPath: number[]
  itemsMap: Record<number, StaticItemData>
}>()

interface LaidOutNode {
  itemId: number
  games: number
  /** Fraction in [0..1] — share of games at this slot that picked this item. Undefined on the synthetic root (no parent context). */
  pickRate?: number
  isMainEdge: boolean
  x: number
  y: number
  children: LaidOutNode[]
}

const ITEM_SIZE = 36
const H_GAP = 22
const V_GAP = 44
const MAX_CHILDREN = 4

const layout = computed(() => {
  // The highlighted (solid) edges trace the backend's core "Build path"
  // verbatim: we walk `props.itemPath` — the canonical progression the backend
  // already computed and gated at its 20% confidence threshold — and light up
  // the edge into each successive item. Driving the highlight from itemPath
  // (instead of re-deriving a "most popular child" walk here) keeps the solid
  // line in sync with the core build and, crucially, stops it exactly where the
  // core stops: a child can clear the tree's 5% prune yet sit below the path's
  // 20% gate, so it stays visible but un-highlighted.
  function wrapChildren(
    nodes: BuildTreeNode[],
    parentIsMainPath: boolean,
    parentDepth: number,
  ): LaidOutNode[] {
    // Mirror the backend's prune ordering (games desc → wins desc → itemId
    // asc) explicitly rather than trusting serialization order. Backend keeps
    // up to 6 children per node, the UI caps at 4 — the 2 we drop are the
    // lowest by wins/itemId within each tied-games group.
    const sorted = nodes
      .slice()
      .sort((a, b) =>
        b.games - a.games
        || b.wins - a.wins
        || a.itemId - b.itemId,
      )
      .slice(0, MAX_CHILDREN)
    // path[d] is the main item at depth d (path[0] is the root). The child
    // continuing the main path is path[parentDepth + 1], and only while the
    // parent itself sits on the main path. Once itemPath is exhausted there's
    // no next id, so every deeper edge falls back to a dashed branch.
    const mainChildId = parentIsMainPath
      ? (props.itemPath[parentDepth + 1] ?? null)
      : null
    return sorted.map(node => ({
      itemId: node.itemId,
      games: node.games,
      pickRate: node.pickRate,
      isMainEdge: mainChildId !== null && node.itemId === mainChildId,
      x: 0,
      y: 0,
      children: wrapChildren(
        node.children,
        mainChildId !== null && node.itemId === mainChildId,
        parentDepth + 1,
      ),
    }))
  }

  const root: LaidOutNode = {
    itemId: props.firstItemId,
    games: 0,
    // Root is the build's first item by definition — every path in this
    // sub-tree starts here, so its slot-1 pickrate is 100%.
    pickRate: 1,
    isMainEdge: false,
    x: 0,
    y: 0,
    children: wrapChildren(props.tree, true, 0),
  }

  function widthOf(node: LaidOutNode): number {
    if (node.children.length === 0) return ITEM_SIZE
    const childrenWidth = node.children.reduce(
      (sum, child, i) => sum + widthOf(child) + (i > 0 ? H_GAP : 0),
      0,
    )
    return Math.max(ITEM_SIZE, childrenWidth)
  }

  function position(node: LaidOutNode, leftX: number, depth: number) {
    const subWidth = widthOf(node)
    node.x = leftX + subWidth / 2
    node.y = depth * (ITEM_SIZE + V_GAP) + ITEM_SIZE / 2

    if (node.children.length === 0) return

    const childrenTotal = node.children.reduce(
      (sum, child, i) => sum + widthOf(child) + (i > 0 ? H_GAP : 0),
      0,
    )
    let cursor = leftX + (subWidth - childrenTotal) / 2
    for (const child of node.children) {
      const cw = widthOf(child)
      position(child, cursor, depth + 1)
      cursor += cw + H_GAP
    }
  }

  position(root, 0, 0)

  const flat: LaidOutNode[] = []
  function collect(node: LaidOutNode) {
    flat.push(node)
    node.children.forEach(collect)
  }
  collect(root)

  const width = widthOf(root)
  const height = Math.max(...flat.map(n => n.y)) + ITEM_SIZE / 2

  // Dashed edges drawn first, solid main-path edges last so the main line
  // always stays on top — otherwise a sibling's dashed line crossing the main
  // path leaves it looking faded.
  const edges = flat.flatMap(parent => parent.children.map(child => ({ parent, child })))
  edges.sort((a, b) => Number(a.child.isMainEdge) - Number(b.child.isMainEdge))

  return { flat, width, height, edges }
})

const hasNodes = computed(() => layout.value.flat.length > 1)
</script>

<template>
  <SectionCard title="Build tree">
    <div
      v-if="hasNodes"
      class="overflow-x-auto"
    >
      <div
        class="relative mx-auto"
        :style="{ width: `${layout.width}px`, height: `${layout.height}px` }"
      >
        <svg
          class="absolute inset-0 overflow-visible"
          :width="layout.width"
          :height="layout.height"
        >
          <path
            v-for="(edge, edgeIndex) in layout.edges"
            :key="`edge-${edgeIndex}`"
            :d="`M ${edge.parent.x} ${edge.parent.y + ITEM_SIZE / 2} V ${edge.parent.y + ITEM_SIZE / 2 + V_GAP / 2} H ${edge.child.x} V ${edge.child.y - ITEM_SIZE / 2}`"
            fill="none"
            stroke="currentColor"
            :stroke-width="1.5"
            :class="edge.child.isMainEdge ? 'text-default' : 'text-muted/60'"
            :stroke-dasharray="edge.child.isMainEdge ? undefined : '4 4'"
          />
        </svg>
        <GameTooltipItemIcon
          v-for="(node, index) in layout.flat"
          :key="`node-${index}`"
          :item="itemsMap[node.itemId] ?? null"
          :pick-rate="node.pickRate"
          :width="ITEM_SIZE"
          :height="ITEM_SIZE"
          class="absolute rounded"
          :style="{
            left: `${node.x - ITEM_SIZE / 2}px`,
            top: `${node.y - ITEM_SIZE / 2}px`,
            width: `${ITEM_SIZE}px`,
            height: `${ITEM_SIZE}px`,
          }"
        />
      </div>
    </div>
    <p
      v-else
      class="text-sm text-muted"
    >
      No build data
    </p>
  </SectionCard>
</template>
