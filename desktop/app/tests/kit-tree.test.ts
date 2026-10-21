import { describe, expect, it } from 'vitest'
import type { KitNode } from '#shared/types/kit'
import {
  addChild,
  blockedItems,
  chainOf,
  cutAt,
  heldPath,
  itemsTo,
  kitOrder,
  kitPaths,
  maxDepth,
  nextKitStep,
  nodeAt,
  removeSubtree,
  swapSiblings,
  replaceItem,
  treeDepth,
} from '~/utils/kit-tree'
import { layoutKitTree, siblingAt } from '~/utils/kit-tree-layout'

const n = (itemId: number, ...children: KitNode[]): KitNode => ({ itemId, children })

// Luden's → (Lich Bane → Rabadon's) | (Shadowflame → Zhonya's)
const LUDENS = 6655
const LICH = 3100
const RABADON = 3089
const SHADOWFLAME = 4645
const ZHONYA = 3157
const SORC = 3020
const tree = [n(LUDENS, n(LICH, n(RABADON)), n(SHADOWFLAME, n(ZHONYA)))]

describe('the tree', () => {
  it('trades two siblings, each with what follows it', () => {
    const swapped = swapSiblings(tree, [0, 1], 0)
    expect(kitPaths(swapped)).toEqual([[LUDENS, SHADOWFLAME, ZHONYA], [LUDENS, LICH, RABADON]])
    expect(swapSiblings(tree, [0, 1], 1)).toBe(tree)
    expect(swapSiblings(tree, [0, 1], 5)).toEqual(tree)
  })

  it('lists every path, the default first', () => {
    expect(kitPaths(tree)).toEqual([[LUDENS, LICH, RABADON], [LUDENS, SHADOWFLAME, ZHONYA]])
    expect(kitPaths([])).toEqual([])
  })

  it('measures its depth and leaves the boots their slot', () => {
    expect(treeDepth(tree)).toBe(3)
    expect(maxDepth([SORC])).toBe(5)
    expect(maxDepth([])).toBe(6)
  })

  it('finds a node and the items on the way to it', () => {
    expect(nodeAt(tree, [0, 1])?.itemId).toBe(SHADOWFLAME)
    expect(nodeAt(tree, [0, 5])).toBeNull()
    expect(itemsTo(tree, [0, 1, 0])).toEqual([LUDENS, SHADOWFLAME, ZHONYA])
    expect(itemsTo(tree, [0, 9])).toEqual([LUDENS])
  })

  it('blocks what would repeat an item on a path', () => {
    expect(blockedItems(tree, [0, 1], 'child')).toEqual([LUDENS, SHADOWFLAME])
    // Replacing Shadowflame: Luden's above it and Zhonya's under it.
    expect(blockedItems(tree, [0, 1], 'replace')).toEqual([LUDENS, ZHONYA])
  })

  it('replaces, adds and removes without touching the tree it was given', () => {
    const replaced = replaceItem(tree, [0, 0], 3135)
    expect(nodeAt(replaced, [0, 0])?.itemId).toBe(3135)
    expect(nodeAt(tree, [0, 0])?.itemId).toBe(LICH)
    expect(replaceItem(tree, [], 1)).toBe(tree)

    const added = addChild(tree, [0], 3135)
    expect(nodeAt(added, [0])?.children.map(child => child.itemId)).toEqual([LICH, SHADOWFLAME, 3135])
    expect(addChild([], [], LUDENS)).toEqual([n(LUDENS)])

    const removed = removeSubtree(tree, [0, 0])
    expect(kitPaths(removed)).toEqual([[LUDENS, SHADOWFLAME, ZHONYA]])
    expect(removeSubtree(tree, [])).toBe(tree)
  })

  it('makes a chain out of a path and cuts a tree to a depth', () => {
    expect(chainOf([1, 2, 3])).toEqual([n(1, n(2, n(3)))])
    expect(cutAt(tree, 2)).toEqual([n(LUDENS, n(LICH), n(SHADOWFLAME))])
    expect(cutAt(tree, 0)).toEqual([])
  })
})

describe('the walk from the held items', () => {
  it('starts on the first item, then follows the first child', () => {
    expect(nextKitStep(tree, [SORC], [], [])).toEqual({ itemId: LUDENS, isBoots: false })
    expect(nextKitStep(tree, [SORC], [LUDENS], [LUDENS])).toEqual({ itemId: LICH, isBoots: false })
  })

  it('follows the branch the held items took', () => {
    const held = [LUDENS, SORC, SHADOWFLAME]
    expect(heldPath(tree, held, held).map(node => node.itemId)).toEqual([LUDENS, SHADOWFLAME])
    expect(nextKitStep(tree, [SORC], held, held)).toEqual({ itemId: ZHONYA, isBoots: false })
  })

  it('takes the branch completed first when both were built', () => {
    expect(nextKitStep(tree, [SORC], [LUDENS, SHADOWFLAME, LICH], [LUDENS, LICH, SHADOWFLAME])).toEqual({ itemId: ZHONYA, isBoots: false })
    // An item held but never noted in the order counts after the noted ones.
    expect(heldPath(tree, [LUDENS, LICH], [LUDENS, SHADOWFLAME, LICH]).map(node => node.itemId)).toEqual([LUDENS, LICH])
  })

  it('asks for the boots once the branch is done, then has nothing left', () => {
    const done = [LUDENS, LICH, RABADON]
    expect(nextKitStep(tree, [SORC], done, done)).toEqual({ itemId: SORC, isBoots: true })
    expect(nextKitStep(tree, [SORC], done, [...done, SORC])).toBeNull()
    expect(nextKitStep(tree, [], done, done)).toBeNull()
  })

  it('takes any of its boots as done, and shows the ones held', () => {
    const MERCS = 3111
    const done = [LUDENS, LICH, RABADON]
    expect(nextKitStep(tree, [SORC, MERCS], done, [...done, MERCS])).toBeNull()
    expect(nextKitStep(tree, [SORC, MERCS], done, done)).toEqual({ itemId: SORC, isBoots: true })
    expect(kitOrder(tree, [SORC, MERCS], [LUDENS], [LUDENS, MERCS])[1]).toEqual({ itemId: MERCS, isBoots: true, state: 'Completed' })
  })

  it('lays the kit out as a row, the boots after the first item', () => {
    const held = [LUDENS, SHADOWFLAME]
    expect(kitOrder(tree, [SORC], held, held)).toEqual([
      { itemId: LUDENS, isBoots: false, state: 'Completed' },
      { itemId: SORC, isBoots: true, state: 'Upcoming' },
      { itemId: SHADOWFLAME, isBoots: false, state: 'Completed' },
      { itemId: ZHONYA, isBoots: false, state: 'Next' },
    ])
    expect(kitOrder(tree, [], [], [])).toEqual([
      { itemId: LUDENS, isBoots: false, state: 'Next' },
      { itemId: LICH, isBoots: false, state: 'Upcoming' },
      { itemId: RABADON, isBoots: false, state: 'Upcoming' },
    ])
    expect(kitOrder([], [SORC], [], [])).toEqual([{ itemId: SORC, isBoots: true, state: 'Next' }])
  })
})

describe('the layout', () => {
  it('centres a parent over its branches and marks the default path', () => {
    const layout = layoutKitTree(tree, { size: 10, hGap: 2, vGap: 4 })
    expect(layout.width).toBe(22)
    expect(layout.height).toBe(38)
    const at = (path: number[]) => layout.nodes.find(node => node.at.join() === path.join())!
    expect(at([0]).x).toBe(11)
    expect(at([0, 0]).x).toBe(5)
    expect(at([0, 1]).x).toBe(17)
    expect(layout.nodes.filter(node => node.main).map(node => node.itemId)).toEqual([LUDENS, LICH, RABADON])
    // The default path's edges are drawn last.
    expect(layout.edges.at(-1)!.child.main).toBe(true)
    expect(layoutKitTree([], { size: 10, hGap: 2, vGap: 4 })).toEqual({ nodes: [], edges: [], slots: [], width: 0, height: 0 })
  })

  it('finds the sibling a dragged node is over, and nothing else', () => {
    const layout = layoutKitTree(tree, { size: 10, hGap: 2, vGap: 4 })
    // Lich at x 5, Shadowflame at x 17, on the row at y 19.
    expect(siblingAt(layout, [0, 0], 18, 20, 10)).toBe(1)
    expect(siblingAt(layout, [0, 0], 5, 19, 10)).toBeNull()
    expect(siblingAt(layout, [0, 0], 17, 33, 10)).toBeNull()
    expect(siblingAt(layout, [0, 0, 0], 17, 33, 10)).toBeNull()
  })

  it('ends every row with room on a slot, the default path kept straight', () => {
    const layout = layoutKitTree(tree, { size: 10, hGap: 2, vGap: 4, slotDepth: 3 })
    expect(layout.width).toBe(70)
    expect(layout.height).toBe(38)
    const at = (path: number[]) => layout.nodes.find(node => node.at.join() === path.join())!
    expect([at([0]).x, at([0, 0]).x, at([0, 0, 0]).x]).toEqual([23, 5, 5])
    expect(layout.slots.map(({ parentAt, after, branch, x, y }) => ({ parentAt, after, branch, x, y }))).toEqual([
      { parentAt: [0, 0], after: LICH, branch: true, x: 17, y: 33 },
      { parentAt: [0, 1], after: SHADOWFLAME, branch: true, x: 41, y: 33 },
      { parentAt: [0], after: LUDENS, branch: true, x: 53, y: 19 },
      { parentAt: [], after: null, branch: true, x: 65, y: 5 },
    ])
    // Under a leaf, the slot sits right below it; an empty tree is its first slot alone.
    const leaf = layoutKitTree([n(LUDENS)], { size: 10, hGap: 2, vGap: 4, slotDepth: 2 })
    expect(leaf.slots[0]).toMatchObject({ parentAt: [0], branch: false, x: 5, y: 19 })
    expect(layoutKitTree([], { size: 10, hGap: 2, vGap: 4, slotDepth: 6 })).toMatchObject({ slots: [{ parentAt: [], branch: false, x: 5, y: 5 }], width: 10, height: 10 })
  })
})
