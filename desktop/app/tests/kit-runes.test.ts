import { describe, expect, it } from 'vitest'
import type { RuneTreeResponse } from '#shared/types/static-data'
import { defaultRunePage, runeIssues, withPrimaryPerk, withPrimaryStyle, withSecondaryPerk, withSecondaryStyle, withShard } from '~/utils/kit-runes'
import { PAGE, RUNE_TREE } from './kit-fixtures'

describe('a kit rune page', () => {
  it('starts on each row\'s first rune, on two trees', () => {
    const page = defaultRunePage(RUNE_TREE)!
    expect(page).toMatchObject({
      primaryStyleId: 8100,
      primaryKeystoneId: 8112,
      primaryPerk1Id: 8126,
      secondaryStyleId: 8200,
      secondaryPerk1Id: 8224,
      secondaryPerk2Id: 8210,
      statOffense: 5008,
      statFlex: 5008,
      statDefense: 5011,
    })
    expect(runeIssues(page, RUNE_TREE)).toEqual([])
    expect(defaultRunePage({ ...RUNE_TREE, styles: RUNE_TREE.styles.slice(0, 1) })).toBeNull()
  })

  it('resets a primary tree changed, and moves the secondary out of its way', () => {
    const page = withPrimaryStyle(PAGE, RUNE_TREE, 8200)
    expect(page).toMatchObject({ primaryStyleId: 8200, primaryKeystoneId: 8214, primaryPerk3Id: 8237, secondaryStyleId: 8100, secondaryPerk1Id: 8126 })
    expect(withPrimaryStyle(PAGE, RUNE_TREE, 8000)).toMatchObject({ primaryStyleId: 8000, secondaryStyleId: 8200, secondaryPerk1Id: 8226 })
    expect(withPrimaryStyle(PAGE, RUNE_TREE, 1)).toBe(PAGE)
  })

  it('never takes the primary tree as the secondary', () => {
    expect(withSecondaryStyle(PAGE, RUNE_TREE, 8100)).toBe(PAGE)
    expect(withSecondaryStyle(PAGE, RUNE_TREE, 8000)).toMatchObject({ secondaryStyleId: 8000, secondaryPerk1Id: 9101, secondaryPerk2Id: 9104 })
  })

  it('puts a primary rune on its row, the keystone included', () => {
    expect(withPrimaryPerk(PAGE, RUNE_TREE, 8128).primaryKeystoneId).toBe(8128)
    expect(withPrimaryPerk(PAGE, RUNE_TREE, 8143).primaryPerk1Id).toBe(8143)
    expect(withPrimaryPerk(PAGE, RUNE_TREE, 8120).primaryPerk2Id).toBe(8120)
    expect(withPrimaryPerk(PAGE, RUNE_TREE, 8106).primaryPerk3Id).toBe(8106)
    expect(withPrimaryPerk(PAGE, RUNE_TREE, 9101)).toBe(PAGE)
  })

  it('keeps the two secondary runes on two rows, as the client does', () => {
    // Same row as the first: replaces it.
    expect(withSecondaryPerk(PAGE, RUNE_TREE, 8275)).toMatchObject({ secondaryPerk1Id: 8275, secondaryPerk2Id: 8210 })
    // Same row as the second: replaces it.
    expect(withSecondaryPerk(PAGE, RUNE_TREE, 8234)).toMatchObject({ secondaryPerk1Id: 8226, secondaryPerk2Id: 8234 })
    // A third row: the older goes, the two kept in row order.
    expect(withSecondaryPerk(PAGE, RUNE_TREE, 8236)).toMatchObject({ secondaryPerk1Id: 8210, secondaryPerk2Id: 8236 })
    expect(withSecondaryPerk(PAGE, RUNE_TREE, 8112)).toBe(PAGE)
  })

  it('takes a shard on its own row only', () => {
    expect(withShard(PAGE, RUNE_TREE, 1, 5010).statFlex).toBe(5010)
    expect(withShard(PAGE, RUNE_TREE, 2, 5013).statDefense).toBe(5013)
    expect(withShard(PAGE, RUNE_TREE, 0, 5013)).toBe(PAGE)
    expect(withShard(PAGE, RUNE_TREE, 3, 5008)).toBe(PAGE)
  })

  it('says what is wrong with a page', () => {
    expect(runeIssues(PAGE, RUNE_TREE)).toEqual([])
    expect(runeIssues({ ...PAGE, primaryKeystoneId: 8214 }, RUNE_TREE)).toEqual(['The keystone must belong to the primary tree.'])
    expect(runeIssues({ ...PAGE, primaryPerk2Id: 8139 }, RUNE_TREE)).toEqual(['Choose one rune on each row of the primary tree.'])
    expect(runeIssues({ ...PAGE, secondaryStyleId: 8100 }, RUNE_TREE)).toEqual(['Choose a secondary tree other than the primary one.'])
    expect(runeIssues({ ...PAGE, secondaryPerk2Id: 8224 }, RUNE_TREE)).toEqual(['Choose two secondary runes on two different rows.'])
    expect(runeIssues({ ...PAGE, statFlex: 5011 }, RUNE_TREE)).toEqual(['Choose a shard on each row.'])
    expect(runeIssues({ ...PAGE, primaryStyleId: 1 }, RUNE_TREE)).toEqual(['Choose a primary rune tree.'])
    const pageless: RuneTreeResponse = { ...RUNE_TREE, styles: [] }
    expect(withSecondaryPerk(PAGE, pageless, 8226)).toBe(PAGE)
    expect(withPrimaryPerk(PAGE, pageless, 8128)).toBe(PAGE)
  })
})
