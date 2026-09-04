import type { ChampionItemContextAxis, ChampionItemContextItem } from '~~/shared/types/item-context'
import { describe, expect, it } from 'vitest'
import {
  indexItemContext,
  itemContextAxisPhrase,
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
      expect(itemContextAxisPhrase(axis({ axis: name, bucket: 'High' })), `${name} High`).toBeTruthy()
      expect(itemContextAxisPhrase(axis({ axis: name, bucket: 'Low' })), `${name} Low`).toBeTruthy()
    }
  })

  it('knows exactly the axes the backend defines — no more, no fewer', () => {
    expect([...KNOWN_ITEM_CONTEXT_AXES].sort()).toEqual([...BACKEND_AXES].sort())
  })

  it('words the low end as its own situation, never as a negation', () => {
    expect(itemContextAxisPhrase(axis({ axis: 'EnemyMelee', bucket: 'Low' })))
      .toBe('against a mostly ranged team')
    expect(itemContextAxisPhrase(axis({ axis: 'OpponentRanged', bucket: 'Low' })))
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

describe('indexItemContext scope note', () => {
  it('attaches the all-matchups clause when a matchup is pinned', () => {
    const item = { slot: 'Build', itemId: 3065 } as ChampionItemContextItem

    const pinned = indexItemContext([item], { allMatchups: true })
    expect(pinned.get(itemContextKey('Build', 3065))?.scopeNote).toBe('all matchups')
  })

  it('says nothing extra when no matchup is pinned', () => {
    const item = { slot: 'Build', itemId: 3065 } as ChampionItemContextItem

    expect(indexItemContext([item]).get(itemContextKey('Build', 3065))?.scopeNote).toBeUndefined()
    expect(indexItemContext([item], { allMatchups: false })
      .get(itemContextKey('Build', 3065))?.scopeNote).toBeUndefined()
  })

  it('does not mutate the payload it was handed', () => {
    const item = { slot: 'Build', itemId: 3065 } as ChampionItemContextItem

    indexItemContext([item], { allMatchups: true })

    expect('scopeNote' in item).toBe(false)
  })
})
