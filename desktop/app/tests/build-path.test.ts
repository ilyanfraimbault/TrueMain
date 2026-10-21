import { describe, expect, it } from 'vitest'
import type { StaticItemData } from '#shared/types/static-data'
import type { ItemRouteStep } from '~/utils/build-path'
import { buildPath } from '~/utils/build-path'

function item(id: number, totalGold: number, from?: number[]): StaticItemData {
  return { id, name: `Item ${id}`, iconUrl: '', totalGold, ...(from ? { from } : {}) }
}

const ARCHANGELS = 3003
const TEAR = 3070
const LOST_CHAPTER = 3802
const CODEX = 3108
const TOME = 1052
const SAPPHIRE = 1027
const MOTE = 2022
const ODD = 9001

const items: Record<number, StaticItemData> = {
  [ARCHANGELS]: item(ARCHANGELS, 2900, [TEAR, LOST_CHAPTER, CODEX]),
  [TEAR]: item(TEAR, 400),
  [LOST_CHAPTER]: item(LOST_CHAPTER, 1200, [TOME, SAPPHIRE, MOTE]),
  [CODEX]: item(CODEX, 850, [TOME, MOTE]),
  [TOME]: item(TOME, 400),
  [SAPPHIRE]: item(SAPPHIRE, 300),
  [MOTE]: item(MOTE, 250),
  [ODD]: item(ODD, 325),
}

function step(itemId: number, rank: number, components: ItemRouteStep[] = []): ItemRouteStep {
  return { itemId, cost: items[itemId]!.totalGold, rank, share: 0.5, measured: true, components }
}

/** The mains' order: the Tear, then Lost Chapter — Tome, Sapphire, Mote — then the Codex. */
const ROUTE: ItemRouteStep[] = [
  step(TEAR, 1),
  step(LOST_CHAPTER, 2, [step(TOME, 1), step(SAPPHIRE, 2), step(MOTE, 3)]),
  step(CODEX, 3, [step(TOME, 1), step(MOTE, 2)]),
]

const states = (path: ReturnType<typeof buildPath>) =>
  path.entries.map(entry => (entry.parts.length
    ? { itemId: entry.itemId, parts: entry.parts.map(part => [part.itemId, part.state]) }
    : [entry.itemId, entry.state]))

describe('buildPath', () => {
  it('walks the gold down the mains\' order, not the cheapest first', () => {
    // 800 gold with the Tear held: Lost Chapter's Tome and Sapphire, in that
    // order — the Mote, cheaper, stays for later because the gold stops before it.
    const path = buildPath(ARCHANGELS, ROUTE, [TEAR], 800, items)

    expect(states(path)).toEqual([
      [TEAR, 'owned'],
      { itemId: LOST_CHAPTER, parts: [[TOME, 'buy'], [SAPPHIRE, 'buy'], [MOTE, 'later']] },
      [CODEX, 'later'],
    ])
    expect(path.measured).toBe(true)
  })

  it('keeps an intermediate whole when the gold buys it whole, or nothing of it', () => {
    expect(states(buildPath(ARCHANGELS, ROUTE, [TEAR], 1250, items))).toEqual([
      [TEAR, 'owned'],
      [LOST_CHAPTER, 'buy'],
      [CODEX, 'later'],
    ])
    expect(states(buildPath(ARCHANGELS, ROUTE, [TEAR], 100, items))).toEqual([
      [TEAR, 'owned'],
      [LOST_CHAPTER, 'later'],
      [CODEX, 'later'],
    ])
  })

  it('opens an intermediate some of whose parts are held, and prices the rest of it from there', () => {
    const path = buildPath(ARCHANGELS, ROUTE, [SAPPHIRE], 500, items)

    expect(states(path)).toEqual([
      [TEAR, 'buy'],
      { itemId: LOST_CHAPTER, parts: [[TOME, 'later'], [SAPPHIRE, 'owned'], [MOTE, 'later']] },
      [CODEX, 'later'],
    ])
    expect(path.entries[1]!.cost).toBe(900)
  })

  it('stops at the first component the gold cannot cover, even when a later one is cheaper', () => {
    const route = [step(CODEX, 1, [step(TOME, 1), step(MOTE, 2)]), step(TEAR, 2)]
    const path = buildPath(ARCHANGELS, route, [TOME, MOTE], 300, items)

    // The Codex's combine (200) is paid, then the Tear (400) does not fit.
    expect(path.entries.map(entry => [entry.itemId, entry.state])).toEqual([[CODEX, 'buy'], [TEAR, 'later']])
  })

  it('says "about" when the 50-gold step straddles the price', () => {
    const odd: Record<number, StaticItemData> = { ...items, 9002: item(9002, 1000, [ODD, TEAR]) }
    const route: ItemRouteStep[] = [
      { itemId: ODD, cost: 325, rank: 1, share: null, measured: true, components: [] },
      { itemId: TEAR, cost: 400, rank: 2, share: null, measured: true, components: [] },
    ]

    // 300 gold reads as 300 to 349: the 325 may be there, the Tear after it is not.
    const path = buildPath(9002, route, [], 300, odd)
    expect(path.entries.map(entry => [entry.itemId, entry.state, entry.approx])).toEqual([
      [ODD, 'buy', true],
      [TEAR, 'later', false],
    ])
    expect(buildPath(9002, route, [], 350, odd).entries[0]!.approx).toBe(false)
  })

  it('falls back to the recipe\'s order, and says so, when the answer carries no route', () => {
    const path = buildPath(ARCHANGELS, undefined, [], 450, items)

    expect(path.measured).toBe(false)
    expect(path.entries.map(entry => entry.itemId)).toEqual([TEAR, LOST_CHAPTER, CODEX])
    expect(path.entries[0]!.state).toBe('buy')
  })

  it('has nothing to walk for an item with no recipe', () => {
    expect(buildPath(TEAR, [], [], 1000, items).entries).toEqual([])
  })
})
