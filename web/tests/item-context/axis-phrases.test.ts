import type { ChampionItemContextAxis, ChampionItemContextItem } from '~~/shared/types/item-context'
import { describe, expect, it } from 'vitest'
import {
  indexItemContext,
  ITEM_CONTEXT_TONE_CLASS,
  itemContextAxisPhrase,
  itemContextAxisText,
  itemContextKey,
  KNOWN_ITEM_CONTEXT_AXES,
  wordableAxes,
} from '~~/shared/utils/item-context'

// The axes the backend can send, from `Data/ItemContext/ItemContextTypes.cs`
// (`ItemContextAxis`, minus the synthetic `Overall` the API never serialises). Kept as a
// hand-maintained mirror on purpose: it is what makes the test below fail when an axis is
// added to the fold without copy for it, instead of that axis silently rendering nothing.
const BACKEND_AXES = [
  'EnemyMagicDamage',
  'EnemyPhysicalDamage',
  'EnemySustain',
  'EnemyCrowdControl',
  'EnemyFrontline',
  'EnemyMelee',
  'EnemyCrit',
  'EnemyArmorPenetration',
  'OpponentRanged',
  'OpponentLanePressure',
  'OpponentMagicDamage',
  'OpponentSustain',
  'AllyMagicDamage',
  'AllyFrontline',
  'OwnGoldLeadAt15',
]

function axis(partial: Partial<ChampionItemContextAxis> & { axis: string }): ChampionItemContextAxis {
  return {
    bucket: 'High',
    draftTime: true,
    gamesIn: 310,
    totalIn: 500,
    gamesOut: 90,
    totalOut: 500,
    rateIn: 0.62,
    rateOut: 0.18,
    lift: 0.44,
    patchWindow: 1,
    ...partial,
  }
}

describe('itemContextAxisPhrase', () => {
  it('words every axis the backend can send, at both ends', () => {
    for (const name of BACKEND_AXES) {
      for (const bucket of ['High', 'Low'] as const) {
        const text = itemContextAxisText(axis({ axis: name, bucket }))
        expect(text, `${name} ${bucket}`).toBeTruthy()
        expect(text, `${name} ${bucket}`).not.toMatch(/undefined|\[object/)
      }
    }
  })

  it('gives every coloured term a class the stylesheet knows', () => {
    for (const name of BACKEND_AXES) {
      for (const bucket of ['High', 'Low'] as const) {
        for (const token of itemContextAxisPhrase(axis({ axis: name, bucket })) ?? []) {
          if (token.tone) expect(ITEM_CONTEXT_TONE_CLASS[token.tone], `${name} ${token.text}`).toBeTruthy()
        }
      }
    }
  })

  it('colours magic damage with the magic-resist cyan, not the ability-power violet', () => {
    // Riot colours damage by the resistance that blocks it, which is the convention the
    // item description directly above this card already follows.
    const tokens = itemContextAxisPhrase(axis({ axis: 'EnemyMagicDamage', bucket: 'High' }))!
    const marked = tokens.find(token => token.tone)!
    expect(marked.text).toBe('magic-damage')
    expect(ITEM_CONTEXT_TONE_CLASS[marked.tone!]).toBe('text-stat-mr')

    const physical = itemContextAxisPhrase(axis({ axis: 'EnemyPhysicalDamage', bucket: 'High' }))!
      .find(token => token.tone)!
    expect(ITEM_CONTEXT_TONE_CLASS[physical.tone!]).toBe('text-stat-ad')
  })

  it('leaves terms with no stat behind them uncoloured', () => {
    // Melee, ranged, a strong laner: real situations, but nothing in the stat vocabulary
    // answers them, and a card where every other word is tinted says nothing.
    for (const name of ['EnemyMelee', 'OpponentRanged', 'OpponentLanePressure']) {
      const tokens = itemContextAxisPhrase(axis({ axis: name, bucket: 'High' }))!
      expect(tokens.some(token => token.tone), name).toBe(false)
    }
  })

  it('knows exactly the axes the backend defines — no more, no fewer', () => {
    expect([...KNOWN_ITEM_CONTEXT_AXES].sort()).toEqual([...BACKEND_AXES].sort())
  })

  it('words the low end as its own situation, never as a negation', () => {
    expect(itemContextAxisText(axis({ axis: 'EnemyMelee', bucket: 'Low' })))
      .toBe('against a mostly ranged team')
    expect(itemContextAxisText(axis({ axis: 'OpponentRanged', bucket: 'Low' })))
      .toBe('against a melee lane opponent')
  })

  it('returns null for an axis this build does not know', () => {
    // A backend ahead of the deployed front end: one line fewer beats a raw identifier.
    expect(itemContextAxisPhrase(axis({ axis: 'EnemyTrueDamage' }))).toBeNull()
  })
})

describe('wordableAxes', () => {
  it('drops the findings the card could not word', () => {
    const item = {
      axes: [axis({ axis: 'EnemyMagicDamage' }), axis({ axis: 'SomethingNew' })],
    } as ChampionItemContextItem

    expect(wordableAxes(item).map(a => a.axis)).toEqual(['EnemyMagicDamage'])
  })
})

describe('indexItemContext', () => {
  it('keys on the slot as well as the item, because the two answer different questions', () => {
    const build = { slot: 'Build', itemId: 3111 } as ChampionItemContextItem
    const boots = { slot: 'Boots', itemId: 3111 } as ChampionItemContextItem

    const index = indexItemContext([build, boots])

    expect(index.get(itemContextKey('Build', 3111))).toBe(build)
    expect(index.get(itemContextKey('Boots', 3111))).toBe(boots)
    expect(index.size).toBe(2)
  })

  it('tolerates a missing payload', () => {
    expect(indexItemContext(null).size).toBe(0)
    expect(indexItemContext(undefined).size).toBe(0)
  })
})
