import { afterEach, describe, expect, it, vi } from 'vitest'
import type { RuneSheet, RuneTreeResponse, StaticPerkData } from '~~/shared/types/static-data'
import {
  findRuneSheet,
  runeSheetCellOrigin,
  runeSheetGrid,
  runeSheetGroups,
  runeSheetIconStyle,
  runeSheetSize,
} from '~~/shared/utils/rune-sheet'

const CDRAGON = 'https://raw.communitydragon.org/16.19/plugins/rcp-be-lol-game-data/global/default/v1/perk-images'

function perk(id: number, iconUrl = `${CDRAGON}/${id}.png`): StaticPerkData {
  return { id, name: `Perk ${id}`, iconUrl }
}

function tree(): RuneTreeResponse {
  const ids = [8005, 8008, 8021, 8010, 9101, 9111, 8009, 9104, 9105, 9103, 8014, 8017, 8299,
    8112, 8128, 9923, 8126, 8139, 8143, 8137, 8140, 8141, 8135, 8105, 8106,
    5008, 5005, 5007, 5010, 5001, 5011, 5013]
  return {
    styles: [
      { styleId: 8000, name: 'Precision', iconUrl: '', keystones: [8005, 8008, 8021, 8010], subRows: [[9101, 9111, 8009], [9104, 9105, 9103], [8014, 8017, 8299]] },
      { styleId: 8100, name: 'Domination', iconUrl: '', keystones: [8112, 8128, 9923], subRows: [[8126, 8139, 8143], [8137, 8140, 8141], [8135, 8105, 8106]] },
    ],
    perks: Object.fromEntries(ids.map(id => [id, perk(id)])),
    perkStyles: {},
    shardSlots: [[5008, 5005, 5007], [5008, 5010, 5001], [5011, 5013, 5001]],
  }
}

function sheet(overrides: Partial<RuneSheet> = {}): RuneSheet {
  return { url: '/_rune-sheet/16.19/abc.webp', cell: 64, gap: 2, columns: 4, rows: 4, perkIds: [1, 2, 3, 4, 5, 6], ...overrides }
}

describe('runeSheetGroups', () => {
  it('gives each style its own sheet, keystones first, and the shards one more', () => {
    const groups = runeSheetGroups(tree())
    expect(groups).toHaveLength(3)
    expect(groups[0]).toEqual([8005, 8008, 8021, 8010, 9101, 9111, 8009, 9104, 9105, 9103, 8014, 8017, 8299])
    expect(groups[1]?.slice(0, 3)).toEqual([8112, 8128, 9923])
  })

  it('holds each shard once, though shard rows repeat them', () => {
    expect(runeSheetGroups(tree())[2]).toEqual([5008, 5005, 5007, 5010, 5001, 5011, 5013])
  })

  it('leaves out perks with no icon, which the component draws the usual way', () => {
    const t = tree()
    delete t.perks[9923]
    t.perks[8126] = perk(8126, '')
    expect(runeSheetGroups(t)[1]).not.toContain(9923)
    expect(runeSheetGroups(t)[1]).not.toContain(8126)
  })
})

describe('sheet geometry', () => {
  it('lays a style out on a near-square grid', () => {
    expect(runeSheetGrid(13)).toEqual({ columns: 4, rows: 4 })
    expect(runeSheetGrid(12)).toEqual({ columns: 4, rows: 3 })
    expect(runeSheetGrid(7)).toEqual({ columns: 3, rows: 3 })
    expect(runeSheetGrid(1)).toEqual({ columns: 1, rows: 1 })
  })

  it('places cells row by row, a gap apart, with no gap past the last one', () => {
    const s = sheet()
    expect(runeSheetCellOrigin(s, 0)).toEqual({ x: 0, y: 0 })
    expect(runeSheetCellOrigin(s, 3)).toEqual({ x: 198, y: 0 })
    expect(runeSheetCellOrigin(s, 5)).toEqual({ x: 66, y: 66 })
    expect(runeSheetSize(s)).toEqual({ width: 262, height: 262 })
  })
})

describe('runeSheetIconStyle', () => {
  it('draws the cell at its native size unscaled', () => {
    expect(runeSheetIconStyle(sheet(), 6, 64)).toEqual({
      backgroundImage: 'url("/_rune-sheet/16.19/abc.webp")',
      backgroundSize: '262px 262px',
      backgroundPosition: '-66px -66px',
      backgroundRepeat: 'no-repeat',
    })
  })

  it('scales the whole sheet so the cell fills a smaller box', () => {
    const style = runeSheetIconStyle(sheet(), 6, 32)
    expect(style?.backgroundSize).toBe('131px 131px')
    expect(style?.backgroundPosition).toBe('-33px -33px')
  })

  it('never writes a negative zero for the first cell', () => {
    expect(runeSheetIconStyle(sheet(), 1, 39)?.backgroundPosition).toBe('0px 0px')
  })

  it('has nothing for a perk the sheet does not hold', () => {
    expect(runeSheetIconStyle(sheet(), 99, 32)).toBeNull()
  })
})

describe('findRuneSheet', () => {
  it('finds the sheet holding the perk, and none when no sheet does', () => {
    const a = sheet({ url: 'a', perkIds: [1, 2] })
    const b = sheet({ url: 'b', perkIds: [3] })
    expect(findRuneSheet([a, b], 3)).toBe(b)
    expect(findRuneSheet([a, b], 4)).toBeNull()
    expect(findRuneSheet(undefined, 1)).toBeNull()
  })
})

describe('describeRuneSheets', () => {
  async function load() {
    vi.resetModules()
    vi.stubGlobal('$fetch', vi.fn())
    vi.stubGlobal('createError', (opts: { statusMessage?: string }) => new Error(opts.statusMessage))
    vi.stubGlobal('defineCachedFunction', (fn: unknown) => fn)
    return (await import('~~/server/utils/rune-tree')).describeRuneSheets
  }

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('describes one sheet per group, at a URL under the requested patch', async () => {
    const describeRuneSheets = await load()
    const sheets = describeRuneSheets(tree(), '16.19')
    expect(sheets).toHaveLength(3)
    expect(sheets[0]).toMatchObject({ cell: 64, gap: 2, columns: 4, rows: 4 })
    for (const s of sheets) expect(s.url).toMatch(/^\/_rune-sheet\/16\.19\/[0-9a-f]{16}\.webp$/)
    expect(describeRuneSheets(tree(), null)[0]?.url).toMatch(/^\/_rune-sheet\/latest\//)
  })

  it('keeps a URL while the drawing stays the same, and changes it when an icon moves', async () => {
    const describeRuneSheets = await load()
    const before = describeRuneSheets(tree(), '16.19')
    expect(describeRuneSheets(tree(), '16.19')).toEqual(before)

    const changed = tree()
    changed.perks[8005] = perk(8005, `${CDRAGON}/moved.png`)
    const after = describeRuneSheets(changed, '16.19')
    expect(after[0]?.url).not.toBe(before[0]?.url)
    expect(after[1]?.url).toBe(before[1]?.url)
  })
})
