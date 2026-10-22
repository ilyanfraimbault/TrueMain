import type { KitNode } from '#shared/types/kit'

/** A node placed: its address in the tree (`kit-tree.ts`), its item, its centre in px. */
export interface PlacedKitNode {
  at: number[]
  /**
   * Who the node is rather than where: the items from its root down to it when
   * first drawn, then kept for as long as the node is (through `keys`). It
   * survives a swap, so a drawn node keeps its element and glides to its new
   * place instead of being redrawn there.
   */
  key: string
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
  /** The parent's `key` (`''` for the roots): the slot follows it through a swap. */
  parentKey: string
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
 * The node the one at `at`, dragged so its centre is at (`x`, `y`), would trade
 * places with: the nearest of those `accepts` lets it take, within an icon of
 * its centre — the gaps between nodes count as the nearest one's. `null` while
 * the dragged node is still nearest its own place, or nowhere near another.
 */
export function dropTargetAt(layout: KitTreeLayout, at: number[], x: number, y: number, size: number, accepts: (to: number[]) => boolean): number[] | null {
  const own = at.join('.')
  const distance = (node: PlacedKitNode) => Math.hypot(node.x - x, node.y - y)
  const nearest = layout.nodes
    .filter(node => node.at.join('.') === own || accepts(node.at))
    .reduce<PlacedKitNode | null>((best, node) => (distance(node) <= size && (!best || distance(node) < distance(best)) ? node : best), null)
  return !nearest || nearest.at.join('.') === own ? null : nearest.at
}

/** A row measured around its first node's centre: each node's centre, its slot's, and how far it reaches either side. */
interface Row { centres: number[], slot: number | null, left: number, right: number }

/**
 * The kit's tree laid out the way the build tree is (`layoutBuildTree`): each
 * node centred over its branches, rows `size + vGap` apart, siblings `hGap`
 * apart — but in the kit's own order and uncapped, since the first child is
 * the player's default and every branch is theirs. Several roots sit side by
 * side as one forest.
 *
 * With `slotDepth`, every row of `slotDepth` items or fewer gets a slot for a
 * new item: right beside its last node — another branch — or, for a node with
 * nothing after it, right under it. A slot never moves a node: a parent sits
 * over its own children only, so a single path stays one straight line and
 * only a second branch spreads the tree.
 */
export function layoutKitTree(
  tree: KitNode[],
  { size, hGap, vGap, slotDepth = 0, keys }: {
    size: number
    hGap: number
    vGap: number
    slotDepth?: number
    /** The keys given so far, by node: a node moved with its branch keeps the one it had. */
    keys?: WeakMap<KitNode, string>
  },
): KitTreeLayout {
  const half = size / 2
  /** A row at `depth` (0 for the roots) gets a slot. */
  const slotted = (depth: number) => depth < slotDepth

  // How far each node's subtree reaches left and right of its centre, and where its children sit.
  const shapes = new Map<KitNode, { row: Row, mid: number, left: number, right: number }>()

  function measureRow(level: KitNode[], depth: number): Row {
    if (level.length === 0) return slotted(depth) ? { centres: [], slot: 0, left: half, right: half } : { centres: [], slot: null, left: 0, right: 0 }
    const centres: number[] = []
    level.forEach((node, i) => {
      const shape = measure(node, depth)
      if (i === 0) centres.push(0)
      else {
        const previous = shapes.get(level[i - 1]!)!
        centres.push(centres[i - 1]! + previous.right + hGap + shape.left)
      }
    })
    const last = centres.at(-1)!
    const slot = slotted(depth) ? last + size + hGap : null
    const reach = Math.max(last + shapes.get(level.at(-1)!)!.right, slot === null ? -Infinity : slot + half)
    return { centres, slot, left: shapes.get(level[0]!)!.left, right: reach }
  }

  function measure(node: KitNode, depth: number) {
    const known = shapes.get(node)
    if (known) return known
    const row = measureRow(node.children, depth + 1)
    const mid = row.centres.length ? (row.centres[0]! + row.centres.at(-1)!) / 2 : 0
    const shape = { row, mid, left: Math.max(half, mid + row.left), right: Math.max(half, row.right - mid) }
    shapes.set(node, shape)
    return shape
  }

  const nodes: PlacedKitNode[] = []
  const edges: KitTreeLayout['edges'] = []
  const slots: KitTreeSlot[] = []
  const rowY = (depth: number) => depth * (size + vGap) + half
  const used = new Set<string>()
  /** The node's kept key, else `fresh`; never one already drawn. */
  function keyOf(node: KitNode, fresh: string): string {
    let key = keys?.get(node) ?? fresh
    while (used.has(key)) key += '+'
    used.add(key)
    keys?.set(node, key)
    return key
  }

  /** Places a measured row whose first centre (or lone slot) is at `origin`. */
  function place(level: KitNode[], row: Row, origin: number, depth: number, parent: PlacedKitNode | null, onMain: boolean) {
    const seen = new Map<number, number>()
    level.forEach((node, index) => {
      // Twin siblings (the same item twice) told apart by their order among themselves.
      const twin = seen.get(node.itemId) ?? 0
      seen.set(node.itemId, twin + 1)
      const placed: PlacedKitNode = {
        at: [...(parent?.at ?? []), index],
        key: keyOf(node, `${parent?.key ?? ''}/${node.itemId}${twin ? `~${twin}` : ''}`),
        itemId: node.itemId,
        x: origin + row.centres[index]!,
        y: rowY(depth),
        main: onMain && index === 0,
      }
      nodes.push(placed)
      if (parent) edges.push({ parent, child: placed })
      const shape = shapes.get(node)!
      place(node.children, shape.row, placed.x - shape.mid, depth + 1, placed, placed.main)
    })
    if (row.slot !== null) {
      slots.push({ parentAt: parent?.at ?? [], parentKey: parent?.key ?? '', after: parent?.itemId ?? null, branch: level.length > 0, x: origin + row.slot, y: rowY(depth) })
    }
  }

  const roots = measureRow(tree, 0)
  place(tree, roots, roots.left, 0, null, true)
  edges.sort((a, b) => Number(a.child.main) - Number(b.child.main))

  const ys = [...nodes, ...slots].map(point => point.y)
  const height = ys.length ? Math.max(...ys) + half : 0
  return { nodes, edges, slots, width: roots.left + roots.right, height }
}
