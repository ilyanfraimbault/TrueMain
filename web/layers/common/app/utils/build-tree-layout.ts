import type { BuildTreeNode } from '#shared/types/champions'

export interface LaidOutBuildTreeNode {
  itemId: number
  /** The item this node was built after — 0 on the root, which is the build's first item. */
  parentItemId: number
  games: number
  /** Fraction in [0..1] — share of games at this slot that picked this item. Undefined on the synthetic root (no parent context). */
  pickRate?: number
  isMainEdge: boolean
  x: number
  y: number
  children: LaidOutBuildTreeNode[]
}

export interface BuildTreeLayout {
  /** Every node, root first, in depth-first order. */
  flat: LaidOutBuildTreeNode[]
  width: number
  height: number
  /** Dashed edges first, the main path's last, so the main line stays on top. */
  edges: { parent: LaidOutBuildTreeNode, child: LaidOutBuildTreeNode }[]
}

const MAX_CHILDREN = 4

/**
 * Lays out a champion's build tree (`ChampionBuildTree`): every node centred over
 * its sub-tree, rows `itemSize + vGap` apart, siblings `hGap` apart, in px.
 */
export function layoutBuildTree(
  tree: BuildTreeNode[],
  firstItemId: number,
  itemPath: number[],
  { itemSize, hGap, vGap }: { itemSize: number, hGap: number, vGap: number },
): BuildTreeLayout {
  // The highlighted (solid) edges trace the backend's core "Build path"
  // verbatim: we walk `itemPath` — the canonical progression the backend
  // already computed and gated at its 20% confidence threshold — and light up
  // the edge into each successive item. Driving the highlight from itemPath
  // (instead of re-deriving a "most popular child" walk here) keeps the solid
  // line in sync with the core build and, crucially, stops it exactly where the
  // core stops: a child can clear the tree's prune yet sit below the path's
  // 20% gate, so it stays visible but un-highlighted.
  function wrapChildren(
    nodes: BuildTreeNode[],
    parentIsMainPath: boolean,
    parentDepth: number,
    parentItemId: number,
  ): LaidOutBuildTreeNode[] {
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
      ? (itemPath[parentDepth + 1] ?? null)
      : null
    return sorted.map(node => ({
      itemId: node.itemId,
      parentItemId,
      games: node.games,
      pickRate: node.pickRate,
      isMainEdge: mainChildId !== null && node.itemId === mainChildId,
      x: 0,
      y: 0,
      children: wrapChildren(
        node.children,
        mainChildId !== null && node.itemId === mainChildId,
        parentDepth + 1,
        node.itemId,
      ),
    }))
  }

  const root: LaidOutBuildTreeNode = {
    itemId: firstItemId,
    parentItemId: 0,
    games: 0,
    // Root is the build's first item by definition — every path in this
    // sub-tree starts here, so its slot-1 pickrate is 100%.
    pickRate: 1,
    isMainEdge: false,
    x: 0,
    y: 0,
    children: wrapChildren(tree, true, 0, firstItemId),
  }

  function widthOf(node: LaidOutBuildTreeNode): number {
    if (node.children.length === 0) return itemSize
    const childrenWidth = node.children.reduce(
      (sum, child, i) => sum + widthOf(child) + (i > 0 ? hGap : 0),
      0,
    )
    return Math.max(itemSize, childrenWidth)
  }

  function position(node: LaidOutBuildTreeNode, leftX: number, depth: number) {
    const subWidth = widthOf(node)
    node.x = leftX + subWidth / 2
    node.y = depth * (itemSize + vGap) + itemSize / 2

    if (node.children.length === 0) return

    const childrenTotal = node.children.reduce(
      (sum, child, i) => sum + widthOf(child) + (i > 0 ? hGap : 0),
      0,
    )
    let cursor = leftX + (subWidth - childrenTotal) / 2
    for (const child of node.children) {
      const cw = widthOf(child)
      position(child, cursor, depth + 1)
      cursor += cw + hGap
    }
  }

  position(root, 0, 0)

  const flat: LaidOutBuildTreeNode[] = []
  function collect(node: LaidOutBuildTreeNode) {
    flat.push(node)
    node.children.forEach(collect)
  }
  collect(root)

  const width = widthOf(root)
  const height = Math.max(...flat.map(n => n.y)) + itemSize / 2

  // Dashed edges drawn first, solid main-path edges last so the main line
  // always stays on top — otherwise a sibling's dashed line crossing the main
  // path leaves it looking faded.
  const edges = flat.flatMap(parent => parent.children.map(child => ({ parent, child })))
  edges.sort((a, b) => Number(a.child.isMainEdge) - Number(b.child.isMainEdge))

  return { flat, width, height, edges }
}
