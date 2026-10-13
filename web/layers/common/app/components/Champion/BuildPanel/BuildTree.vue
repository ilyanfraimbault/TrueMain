<script setup lang="ts">
import type { BuildTreeNode } from '#shared/types/champions'
import { layoutBuildTree } from '#common/utils/build-tree-layout'
import type { StaticItemData } from '#shared/types/static-data'
import type { ItemContextCard } from '#shared/utils/item-context'
import { resolveItemContext } from '#shared/utils/item-context'

const props = defineProps<{
  tree: BuildTreeNode[]
  firstItemId: number
  /** The backend's core "Build path" (itemIds, starting at firstItemId). The
   * solid main-edge highlight traces this exactly, so it stops where the core
   * path stops rather than running to the deepest leaf. */
  itemPath: number[]
  itemsMap: Record<number, StaticItemData>
  /** Situational verdicts (#1451), keyed by slot + branch + item. Absent where the page has no slice for them. */
  itemContext?: Map<string, ItemContextCard>
  /**
   * The node size and the gaps between nodes, in px — the site's by default. The
   * desktop app's narrow build pane draws a smaller, tighter tree (#1671).
   */
  itemSize?: number
  hGap?: number
  vGap?: number
}>()

/**
 * The verdict for a node, or undefined. Every node here is a completed legendary, so the
 * lookup is always on the `Build` slot — the boots and starter verdicts belong to the
 * variation rows, which ask different questions of the same hover card.
 *
 * A verdict is about the *edge* into a node, not the node's item (#1496): the parent is part
 * of the key, and the tree is the one surface that has it for free. The root node is the
 * build's first item, on branch 0.
 */
function contextFor(itemId: number, parentItemId: number): ItemContextCard | undefined {
  return resolveItemContext(props.itemContext, 'Build', itemId, parentItemId)
}

// Read once: a call site passes constants.
const ITEM_SIZE = props.itemSize ?? 36
const H_GAP = props.hGap ?? 22
const V_GAP = props.vGap ?? 44

const layout = computed(() => layoutBuildTree(props.tree, props.firstItemId, props.itemPath, {
  itemSize: ITEM_SIZE,
  hGap: H_GAP,
  vGap: V_GAP,
}))

const hasNodes = computed(() => layout.value.flat.length > 1)
</script>

<template>
  <!-- No SectionCard (#1466): the tree carries its own visual language — the
       item icons and the edges between them say "build tree" without a heading,
       and a card around it made the panel read as four framed boxes stacked on
       each other. It sits directly under the core block, as the picture of the
       path the core block states in words. -->
  <div
    v-if="hasNodes"
    class="overflow-x-auto"
  >
    <div
      class="relative mx-auto"
      :style="{ width: `${layout.width}px`, height: `${layout.height}px` }"
    >
      <!-- Decorative: the edges only trace the spatial relationship between
           the items already rendered (with accessible names) below. The
           main-path sequence they highlight is separately available as an
           ordered, accessible list via BuildPath. -->
      <svg
        class="absolute inset-0 overflow-visible"
        aria-hidden="true"
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
        :context="contextFor(node.itemId, node.parentItemId)"
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
</template>
