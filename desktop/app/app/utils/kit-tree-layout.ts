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

export interface KitTreeLayout {
  nodes: PlacedKitNode[]
  /** Dashed branches first, the default path's edges last, so its line stays on top. */
  edges: { parent: PlacedKitNode, child: PlacedKitNode }[]
  width: number
  height: number
}

/**
 * The kit's tree laid out the way the build tree is (`layoutBuildTree`): each
 * node centred over its branches, rows `size + vGap` apart, siblings `hGap`
 * apart — but in the kit's own order and uncapped, since the first child is
 * the player's default and every branch is theirs. Several roots sit side by
 * side as one forest.
 */
export function layoutKitTree(tree: KitNode[], { size, hGap, vGap }: { size: number, hGap: number, vGap: number }): KitTreeLayout {
  const widthOf = (node: KitNode): number => {
    if (node.children.length === 0) return size
    return Math.max(size, node.children.reduce((sum, child, i) => sum + widthOf(child) + (i > 0 ? hGap : 0), 0))
  }
  const levelWidth = (nodes: KitNode[]) => nodes.reduce((sum, node, i) => sum + widthOf(node) + (i > 0 ? hGap : 0), 0)

  const nodes: PlacedKitNode[] = []
  const edges: KitTreeLayout['edges'] = []

  function place(level: KitNode[], left: number, depth: number, parent: PlacedKitNode | null, parentAt: number[], onMain: boolean) {
    let cursor = left
    level.forEach((node, index) => {
      const width = widthOf(node)
      const placed: PlacedKitNode = {
        at: [...parentAt, index],
        itemId: node.itemId,
        x: cursor + width / 2,
        y: depth * (size + vGap) + size / 2,
        main: onMain && index === 0,
      }
      nodes.push(placed)
      if (parent) edges.push({ parent, child: placed })
      const childrenWidth = levelWidth(node.children)
      place(node.children, cursor + (width - childrenWidth) / 2, depth + 1, placed, placed.at, placed.main)
      cursor += width + hGap
    })
  }

  place(tree, 0, 0, null, [], true)
  edges.sort((a, b) => Number(a.child.main) - Number(b.child.main))

  const height = nodes.length ? Math.max(...nodes.map(node => node.y)) + size / 2 : 0
  return { nodes, edges, width: levelWidth(tree), height }
}
