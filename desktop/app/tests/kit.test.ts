import { describe, expect, it } from 'vitest'
import type { BuildCoreView } from '~/types/build'
import { activeKey, freeName, kitCoreView, kitFromBuild, kitIssues, newKitId, readStore, withBoots } from '~/utils/kit'
import { kitItemGroups, searchKitItems } from '~/utils/kit-items'
import { kitItemSetBlocks } from '~/utils/item-set'
import { item, kit, PAGE, RUNE_TREE } from './kit-fixtures'

const measured = { games: 10, pickRate: 0.5, winRate: 0.5 }

describe('a kit made out of a build', () => {
  const core: BuildCoreView = {
    itemPath: { itemIds: [6655, 3020, 3100, 3089, 3157, 3135, 4645], ...measured },
    boots: { itemIds: [3020], ...measured },
    starterItems: { itemIds: [1056, 2003], ...measured },
    summonerSpells: { spell1Id: 4, spell2Id: 12, ...measured },
    skillOrder: null,
    runePage: { ...PAGE, ...measured },
  }

  it('takes the starter, boots, core path, rune page and spells', () => {
    const made = kitFromBuild({ championId: 103, position: 'MIDDLE', name: 'Mine' }, core, null)!
    expect(made).toMatchObject({ name: 'Mine', championId: 103, position: 'MIDDLE', spells: [4, 12], starter: [1056, 2003], boots: [3020], runes: PAGE })
    expect(made.runes).not.toHaveProperty('games')
    // The boots leave the path, which keeps five items beside them.
    expect(made.tree[0]!.itemId).toBe(6655)
    expect(kitIssues(made, RUNE_TREE)).toEqual([])
  })

  it('fills what the build lacks', () => {
    const made = kitFromBuild({ championId: 103, position: 'MIDDLE', name: 'x'.repeat(60) }, null, PAGE)!
    expect(made).toMatchObject({ spells: [4, 14], starter: [], boots: [], tree: [] })
    expect(made.name).toHaveLength(40)
    expect(kitFromBuild({ championId: 103, position: 'MIDDLE', name: 'x' }, null, null)).toBeNull()
  })
})

describe('a kit', () => {
  it('cuts its paths when it takes boots', () => {
    const deep = kit({ boots: [], tree: [{ itemId: 1, children: [{ itemId: 2, children: [{ itemId: 3, children: [{ itemId: 4, children: [{ itemId: 5, children: [{ itemId: 6, children: [] }] }] }] }] }] }] })
    expect(kitIssues(deep, null)).toEqual([])
    const booted = withBoots(deep, [3020, 3111])
    expect(booted.boots).toEqual([3020, 3111])
    expect(kitIssues(booted, null)).toEqual([])
    expect(kitIssues({ ...deep, boots: [3020] }, null)).toEqual(['A path holds at most 6 items, boots included.'])
  })

  it('says why it cannot be saved', () => {
    expect(kitIssues(kit(), RUNE_TREE)).toEqual([])
    expect(kitIssues(kit({ name: ' ', tree: [], spells: [4, 4] }), RUNE_TREE)).toEqual([
      'Name the kit.',
      'Add a first item.',
      'Choose two different summoner spells.',
    ])
    expect(kitIssues(kit({ tree: [{ itemId: 1, children: [{ itemId: 1, children: [] }] }] }), null)).toEqual(['An item appears twice on one path.'])
    expect(kitIssues(kit({ tree: [{ itemId: 3020, children: [] }] }), null)).toEqual(['The boots go in their own slot, not on a path.'])
    expect(kitIssues(kit({ runes: { ...PAGE, primaryKeystoneId: 8214 } }), RUNE_TREE)).toEqual(['The keystone must belong to the primary tree.'])
  })

  it('draws as a build core with nothing measured', () => {
    const core = kitCoreView(kit())
    expect(core.summonerSpells).toMatchObject({ spell1Id: 4, spell2Id: 14, games: 0 })
    expect(core.boots?.itemIds).toEqual([3020])
    expect(core.starterItems?.itemIds).toEqual([1056, 2003, 2003])
    expect(core.runePage?.primaryKeystoneId).toBe(8112)
    expect(kitCoreView(kit({ boots: [], starter: [] }))).toMatchObject({ boots: null, starterItems: null })
  })

  it('names a copy after the free name', () => {
    expect(freeName('Mid', [])).toBe('Mid')
    expect(freeName('Mid', ['Mid', 'Mid 2'])).toBe('Mid 3')
    expect(activeKey(103, 'MIDDLE')).toBe('103:MIDDLE')
    expect(newKitId()).not.toBe(newKitId())
  })
})

describe('a stored list of kits', () => {
  it('keeps the kits that read and the marks that name them', () => {
    const store = readStore({
      kits: [kit(), kit({ id: 'k2', position: 'TOP' }), { id: 'bad' }, kit({ id: 'k3', runes: { ...PAGE, statFlex: 0 } }), null],
      active: { '103:MIDDLE': 'k1', '103:TOP': 'k1', '103:BOTTOM': 'gone', '103:JUNGLE': 3 },
    })
    expect(store.kits.map(k => k.id)).toEqual(['k1', 'k2'])
    expect(store.active).toEqual({ '103:MIDDLE': 'k1' })
  })

  it('reads a kit leniently', () => {
    const [read] = readStore({ kits: [{ ...kit(), spells: [4], starter: [1056, 'x'], boots: 'no', updatedAt: 3, name: 'y'.repeat(50) }] }).kits
    expect(read).toMatchObject({ spells: [4, 14], starter: [1056], boots: [], updatedAt: new Date(0).toISOString() })
    expect(read!.name).toHaveLength(40)
    // A kit saved with one boots id still reads; a list keeps its order, without repeats, capped.
    expect(readStore({ kits: [{ ...kit(), boots: 3020 }] }).kits[0]!.boots).toEqual([3020])
    expect(readStore({ kits: [{ ...kit(), boots: [3047, 3020, 3047, 3111, 3158, 3006] }] }).kits[0]!.boots).toEqual([3047, 3020, 3111, 3158])
    expect(readStore(null)).toEqual({ kits: [], active: {} })
    expect(readStore({ kits: 'no' })).toEqual({ kits: [], active: {} })
  })
})

describe('a kit as an item set', () => {
  const name = (id: number) => `Item ${id}`

  it('writes the starter and the boots first, then one block per path', () => {
    expect(kitItemSetBlocks(kit(), name, () => true)).toEqual([
      { title: 'Starter', items: [1056, 2003, 2003] },
      { title: 'Boots', items: [3020] },
      { title: 'Core · Item 3089', items: [6655, 3100, 3089] },
      { title: 'Path 2 · Item 4645', items: [6655, 4645] },
    ])
  })

  it('titles a single path "Core" and drops what the patch does not know', () => {
    const single = kit({ starter: [], tree: [{ itemId: 6655, children: [{ itemId: 9999, children: [] }] }] })
    expect(kitItemSetBlocks(single, name, id => id !== 9999)).toEqual([
      { title: 'Boots', items: [3020] },
      { title: 'Core', items: [6655] },
    ])
  })
})

describe('the item picker\'s shop', () => {
  const items = {
    1001: item(1001, { name: 'Boots', totalGold: 300, tags: ['Boots'] }),
    3020: item(3020, { name: 'Sorcerer\'s Shoes', totalGold: 1100, tags: ['Boots'], from: [1001] }),
    3175: item(3175, { name: 'Spellslinger\'s Shoes', tags: ['Boots'], from: [3020] }),
    1052: item(1052, { name: 'Amplifying Tome', totalGold: 400 }),
    3802: item(3802, { name: 'Lost Chapter', totalGold: 1200, from: [1052] }),
    6655: item(6655, { name: 'Luden\'s Echo', from: [3802] }),
    1056: item(1056, { name: 'Doran\'s Ring', totalGold: 400 }),
    2003: item(2003, { name: 'Health Potion', totalGold: 50, tags: ['Consumable'] }),
    3131: item(3131, { name: 'Sword of the Divine', maps: [12] }),
    7132: item(7132, { name: 'Luden\'s Echo', from: [3802] }),
    3133: item(3133, { name: 'Ornn upgrade', inStore: false }),
    223020: item(223020, { name: 'Arena shoes', tags: ['Boots'], from: [1001] }),
  }

  it('groups the Rift\'s shop and leaves the rest out', () => {
    const groups = kitItemGroups(items)
    expect(Object.fromEntries(groups)).toEqual({
      1001: 'component',
      1052: 'component',
      1056: 'starter',
      3020: 'boots',
      3802: 'component',
      6655: 'legendary',
    })
  })

  it('searches by any word of the name, accents ignored, cheapest first', () => {
    const groups = kitItemGroups(items)
    expect(searchKitItems(items, groups, 'ECHO', ['legendary']).map(s => s.items.map(i => i.id))).toEqual([[6655]])
    expect(searchKitItems(items, groups, '', ['component']).flatMap(s => s.items.map(i => i.id))).toEqual([1001, 1052, 3802])
    expect(searchKitItems(items, groups, 'lûden', ['legendary', 'boots']).map(s => s.label)).toEqual(['Legendary'])
    expect(searchKitItems(items, groups, 'nothing', ['legendary'])).toEqual([])
  })
})
