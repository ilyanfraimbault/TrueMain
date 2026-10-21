import type { KitNode } from '#shared/types/kit'
import type { NextItemOrderStep } from '~/utils/next-item'

/**
 * A kit's item tree (#2185): the edits the editor makes, the paths the item set
 * writes, and the walk the next item follows in game. Every edit returns a new
 * tree — the editor's draft is replaced, never mutated in place.
 *
 * A node is addressed by its path of child indexes from the roots: `[0]` is the
 * first root, `[0, 1]` its second child.
 */

/** The most items a path holds, the boots counted: a full build. */
export const MAX_PATH_ITEMS = 6

/** How deep the tree may go: a full build, less the boots' slot when the kit has any. */
export function maxDepth(boots: number[]): number {
  return boots.length > 0 ? MAX_PATH_ITEMS - 1 : MAX_PATH_ITEMS
}

/** Every path from a root to a leaf, in the tree's order: the first is the default path. */
export function kitPaths(tree: KitNode[]): number[][] {
  const paths: number[][] = []
  const walk = (node: KitNode, before: number[]) => {
    const path = [...before, node.itemId]
    if (node.children.length === 0) paths.push(path)
    for (const child of node.children) walk(child, path)
  }
  for (const root of tree) walk(root, [])
  return paths
}

/** The longest path's length. */
export function treeDepth(tree: KitNode[]): number {
  return tree.reduce((deepest, node) => Math.max(deepest, 1 + treeDepth(node.children)), 0)
}

export function nodeAt(tree: KitNode[], at: number[]): KitNode | null {
  let level = tree
  let node: KitNode | null = null
  for (const index of at) {
    node = level[index] ?? null
    if (!node) return null
    level = node.children
  }
  return node
}

/** The items on the way to the node at `at`, itself included. */
export function itemsTo(tree: KitNode[], at: number[]): number[] {
  const items: number[] = []
  let level = tree
  for (const index of at) {
    const node = level[index]
    if (!node) break
    items.push(node.itemId)
    level = node.children
  }
  return items
}

/** The items a node's subtree holds below it, every branch: what a new item there must not repeat. */
function itemsBelow(node: KitNode): number[] {
  return node.children.flatMap(child => [child.itemId, ...itemsBelow(child)])
}

/**
 * The items that cannot go at `at` without a duplicate on one of its paths: the
 * ones above it and, for a replacement, the ones under it. Adding a child at
 * `at` is asking about the new node, whose only path runs through `at`.
 */
export function blockedItems(tree: KitNode[], at: number[], mode: 'replace' | 'child'): number[] {
  if (mode === 'child') return itemsTo(tree, at)
  const node = nodeAt(tree, at)
  return [...itemsTo(tree, at.slice(0, -1)), ...(node ? itemsBelow(node) : [])]
}

/** `tree` with the list at `parent` (the roots for `[]`) passed through `edit`. */
function editLevel(tree: KitNode[], parent: number[], edit: (level: KitNode[]) => KitNode[]): KitNode[] {
  if (parent.length === 0) return edit(tree)
  const [head, ...rest] = parent as [number, ...number[]]
  return tree.map((node, index) => (index === head ? { ...node, children: editLevel(node.children, rest, edit) } : node))
}

export function replaceItem(tree: KitNode[], at: number[], itemId: number): KitNode[] {
  const index = at.at(-1)
  if (index === undefined) return tree
  return editLevel(tree, at.slice(0, -1), level => level.map((node, i) => (i === index ? { ...node, itemId } : node)))
}

/** A new last child of the node at `at`, or a new root for `[]`. */
export function addChild(tree: KitNode[], at: number[], itemId: number): KitNode[] {
  return editLevel(tree, at, level => [...level, { itemId, children: [] }])
}

/**
 * The tree with the node at `at` and its sibling at `index` traded places,
 * each with what follows it: how the player picks which branch comes first —
 * the default one, followed in game.
 */
export function swapSiblings(tree: KitNode[], at: number[], index: number): KitNode[] {
  const from = at.at(-1)
  if (from === undefined || from === index) return tree
  return editLevel(tree, at.slice(0, -1), (level) => {
    const [a, b] = [level[from], level[index]]
    if (!a || !b) return level
    return level.map((node, i) => (i === from ? b : i === index ? a : node))
  })
}

/** The tree without the node at `at` and everything under it. */
export function removeSubtree(tree: KitNode[], at: number[]): KitNode[] {
  const index = at.at(-1)
  if (index === undefined) return tree
  return editLevel(tree, at.slice(0, -1), level => level.filter((_, i) => i !== index))
}

/** A single path as a tree: the shape a kit starts from when made out of a build. */
export function chainOf(itemIds: number[]): KitNode[] {
  return itemIds.reduceRight<KitNode[]>((children, itemId) => [{ itemId, children }], [])
}

/** The tree cut at `depth` items: what a kit keeps when its boots shorten every path. */
export function cutAt(tree: KitNode[], depth: number): KitNode[] {
  if (depth <= 0) return []
  return tree.map(node => ({ itemId: node.itemId, children: cutAt(node.children, depth - 1) }))
}

/**
 * The branch the held items follow: at each level the node held, the earliest
 * completed when several are, until a level holds none of them. `order` is
 * the order the items were completed in; items held but missing from it count
 * after it.
 */
export function heldPath(tree: KitNode[], order: number[], held: number[]): KitNode[] {
  const rank = (itemId: number) => {
    const index = order.indexOf(itemId)
    return index === -1 ? order.length : index
  }
  const holding = new Set(held)
  const path: KitNode[] = []
  let level = tree
  for (;;) {
    const matched = level
      .filter(node => holding.has(node.itemId))
      .sort((a, b) => rank(a.itemId) - rank(b.itemId))[0]
    if (!matched) return path
    path.push(matched)
    level = matched.children
  }
}

/** The next step of a kit in game: a node of its tree, else boots, else nothing — the kit is done. */
export interface KitStep {
  itemId: number
  isBoots: boolean
}

/**
 * What the kit says to complete next from what is held: the next node on the
 * branch the held items follow — its first child when it has several, the
 * kit's own order — then, once the branch is complete, the kit's first boots
 * if none of its boots are
 * not held yet. `null` when nothing is left: the caller falls back to
 * TrueMain's answer.
 */
export function nextKitStep(tree: KitNode[], boots: number[], order: number[], held: number[]): KitStep | null {
  const path = heldPath(tree, order, held)
  const level = path.at(-1)?.children ?? tree
  const next = level[0]
  if (next) return { itemId: next.itemId, isBoots: false }
  const firstBoots = boots[0]
  if (firstBoots && !boots.some(id => held.includes(id))) return { itemId: firstBoots, isBoots: true }
  return null
}

/**
 * The kit as the build row draws it (#2101's row): the branch held so far,
 * then its default continuation, with the boots after the first item — the
 * ones held, else the kit's first — where
 * the item set puts them too, ahead of the rest.
 */
export function kitOrder(tree: KitNode[], boots: number[], order: number[], held: number[]): NextItemOrderStep[] {
  const path = heldPath(tree, order, held).map(node => node.itemId)
  let level = path.length === 0 ? tree : nodeAtItems(tree, path)
  const rest: number[] = []
  while (level[0]) {
    rest.push(level[0].itemId)
    level = level[0].children
  }
  const next = nextKitStep(tree, boots, order, held)
  const stateOf = (itemId: number, done: boolean): NextItemOrderStep['state'] => {
    if (done) return 'Completed'
    return next?.itemId === itemId ? 'Next' : 'Upcoming'
  }
  const steps: NextItemOrderStep[] = [...path, ...rest].map(itemId => ({ itemId, isBoots: false, state: stateOf(itemId, path.includes(itemId)) }))
  const shownBoots = boots.find(id => held.includes(id)) ?? boots[0]
  if (shownBoots) steps.splice(Math.min(1, steps.length), 0, { itemId: shownBoots, isBoots: true, state: stateOf(shownBoots, held.includes(shownBoots)) })
  return steps
}

/** The children under the path of items `items`, walked from the roots. */
function nodeAtItems(tree: KitNode[], items: number[]): KitNode[] {
  let level = tree
  for (const itemId of items) level = level.find(node => node.itemId === itemId)?.children ?? []
  return level
}
