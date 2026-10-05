import { describe, expect, it } from 'vitest'
import type { ChampionItemContextItem } from '#shared/types/item-context'
import type { BuildCoreView, BuildTreeNode } from '~/types/build'
import { hasItems, itemSetBuild, situationalFromContext, situationalFromTree } from '~/utils/item-set'

function verdict(itemId: number, pickRate: number, overrides: Partial<ChampionItemContextItem> = {}): ChampionItemContextItem {
  return { slot: 'Build', parentItemId: 0, itemId, class: 'Situational', games: 100, branchGames: 400, pickRate, winRate: 0.5, patchWindow: 1, axes: [], ...overrides }
}

function node(itemId: number, games: number, children: BuildTreeNode[] = []): BuildTreeNode {
  return { itemId, games, wins: 0, pickRate: 0, children }
}

const measured = { games: 10, pickRate: 0.5, winRate: 0.5 }

describe('situationalFromContext', () => {
  it('keeps situational completed items, most picked first, core left out', () => {
    const items = [
      verdict(3157, 0.2),
      verdict(3135, 0.4),
      verdict(3089, 0.9),
      verdict(6655, 0.8, { class: 'Core' }),
      verdict(3020, 0.7, { slot: 'Boots' }),
      verdict(3102, 0.3, { class: 'Preference' }),
    ]
    expect(situationalFromContext(items, [6655, 3089])).toEqual([3135, 3157])
  })

  it('lists an item judged on several branches once, at its best rate', () => {
    const items = [verdict(3157, 0.1, { parentItemId: 1 }), verdict(3135, 0.3), verdict(3157, 0.5, { parentItemId: 2 })]
    expect(situationalFromContext(items, [])).toEqual([3157, 3135])
  })
})

describe('situationalFromTree', () => {
  it('collects the siblings of the path at each step, most played first', () => {
    // The tree is what followed the first item (6655), which the path starts with.
    const tree = [
      node(4645, 50, [node(3089, 30), node(3157, 12), node(3135, 8)]),
      node(3157, 20),
      node(3165, 5),
    ]
    expect(situationalFromTree(tree, [6655, 4645, 3089])).toEqual([3157, 3135, 3165])
  })

  it('stops where the tree no longer follows the path', () => {
    expect(situationalFromTree([node(3165, 5)], [6655, 4645, 3089])).toEqual([3165])
    expect(situationalFromTree([], [6655])).toEqual([])
  })
})

describe('itemSetBuild', () => {
  const core: BuildCoreView = {
    starterItems: { ...measured, itemIds: [1056, 2003, 2003] },
    boots: { ...measured, itemIds: [3020] },
    itemPath: { ...measured, itemIds: [6655, 999999, 4645] },
    summonerSpells: null,
    skillOrder: null,
    runePage: null,
  }

  it('keeps every block in order and drops ids the patch does not have', () => {
    const build = itemSetBuild(core, [3135, 888888], itemId => itemId < 100000)
    expect(build).toEqual({ starter: [1056, 2003, 2003], boots: [3020], core: [6655, 4645], situational: [3135] })
    expect(hasItems(build)).toBe(true)
  })

  it('is empty when the build has nothing known', () => {
    expect(hasItems(itemSetBuild({ ...core, starterItems: null, boots: null, itemPath: null }, [], () => true))).toBe(false)
  })
})
