import type { KitNode } from '#shared/types/kit'

/** A node placed: its address in the tree (`kit-tree.ts`), its item, its centre in px. */
export interface PlacedKitNode {
  at: number[]
  itemId: number
  x: number
  y: number
  /** On the default path: the first root, then each first child. */
  main: boolean
}

/**
 * Where a new item goes in the editor, centre in px: at the end of the
 * children of the node at `parentAt` (the roots for `[]`) — under a leaf the
 * item that follows it, beside its children another branch.
 */
export interface KitTreeSlot {
  parentAt: number[]
  /** The item the new one follows, `null` for a root. */
  after: number | null
  /** Beside items already there: a new branch rather than the next item. */
  branch: boolean
  x: number
  y: number
}

export interface KitTreeLayout {
  nodes: PlacedKitNode[]
  /** Dashed branches first, the default path's edges last, so its line stays on top. */
  edges: { parent: PlacedKitNode, child: PlacedKitNode }[]
  slots: KitTreeSlot[]
  width: number
  height: number
}

/**
 * The sibling of the node at `at` whose box holds the point (`x`, `y`): where
 * a node dragged along its row would trade places, or `null` off every
 * sibling and on the node itself.
 */
export function siblingAt(layout: KitTreeLayout, at: number[], x: number, y: number, size: number): number | null {
  const parent = at.slice(0, -1).join('.')
  const half = size / 2
  const hit = layout.nodes.find(node =>
    node.at.length === at.length
    && node.at.slice(0, -1).join('.') === parent
    && Math.abs(node.x - x) <= half
    && Math.abs(node.y - y) <= half)
  const index = hit?.at.at(-1)
  return index === undefined || index === at.at(-1) ? null : index
}

/**
 * The kit's tree laid out the way the build tree is (`layoutBuildTree`): each
 * node centred over its branches, rows `size + vGap` apart, siblings `hGap`
 * apart — but in the kit's own order and uncapped, since the first child is
 * the player's default and every branch is theirs. Several roots sit side by
 * side as one forest.
 *
 * With `slotDepth`, every row of `slotDepth` items or fewer ends on a slot
 * for a new item, kept off the centring so the default path stays straight.
 */
export function layoutKitTree(
  tree: KitNode[],
  { size, hGap, vGap, slotDepth = 0 }: { size: number, hGap: number, vGap: number, slotDepth?: number },
): KitTreeLayout {
  /** A row at `depth` (0 for the roots) ends on a slot. */
  const slotted = (depth: number) => depth < slotDepth
  /** The row's nodes, side by side, without its slot. */
  const nodesWidth = (level: KitNode[], depth: number) =>
    level.reduce((sum, node, i) => sum + widthOf(node, depth) + (i > 0 ? hGap : 0), 0)
  /** The whole row: its nodes, then its slot. */
  const rowWidth = (level: KitNode[], depth: number) => {
    const width = nodesWidth(level, depth)
    if (!slotted(depth)) return width
    return width + (level.length > 0 ? hGap : 0) + size
  }
  function widthOf(node: KitNode, depth: number): number {
    return Math.max(size, rowWidth(node.children, depth + 1))
  }

  const nodes: PlacedKitNode[] = []
  const edges: KitTreeLayout['edges'] = []
  const slots: KitTreeSlot[] = []
  const rowY = (depth: number) => depth * (size + vGap) + size / 2

  function place(level: KitNode[], left: number, depth: number, parent: PlacedKitNode | null, onMain: boolean) {
    let cursor = left
    level.forEach((node, index) => {
      const width = widthOf(node, depth)
      const row = rowWidth(node.children, depth + 1)
      const start = cursor + (width - row) / 2
      const placed: PlacedKitNode = {
        at: [...(parent?.at ?? []), index],
        itemId: node.itemId,
        // Over its children, the slot left out; over its slot when it has none.
        x: node.children.length > 0 ? start + nodesWidth(node.children, depth + 1) / 2 : cursor + width / 2,
        y: rowY(depth),
        main: onMain && index === 0,
      }
      nodes.push(placed)
      if (parent) edges.push({ parent, child: placed })
      place(node.children, start, depth + 1, placed, placed.main)
      cursor += width + hGap
    })
    if (slotted(depth)) {
      slots.push({ parentAt: parent?.at ?? [], after: parent?.itemId ?? null, branch: level.length > 0, x: cursor + size / 2, y: rowY(depth) })
    }
  }

  place(tree, 0, 0, null, true)
  edges.sort((a, b) => Number(a.child.main) - Number(b.child.main))

  const ys = [...nodes, ...slots].map(point => point.y)
  const height = ys.length ? Math.max(...ys) + size / 2 : 0
  return { nodes, edges, slots, width: rowWidth(tree, 0), height }
}
