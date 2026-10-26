import { describe, expect, it } from 'vitest'
import type { PersonaListRow } from '~~/shared/types/ops'
import {
  entitySearchGroups,
  isEntityTerm,
  matchChampions,
  parseMatchId,
  PERSONA_RESULT_LIMIT,
} from '~/utils/palette-search'

// What the ⌘K palette recognises in the typed term (#2290), and where each result
// leads: the query each target page reads.

const CHAMPIONS = [
  { name: 'Ahri', iconUrl: 'https://cdn/ahri.png' },
  { name: 'Kai\'Sa', iconUrl: null },
  { name: 'Lee Sin', iconUrl: 'https://cdn/leesin.png' },
  { name: 'Shaco' },
  { name: 'Akali' },
]

function persona(overrides: Partial<PersonaListRow> = {}): PersonaListRow {
  return {
    id: 'p1',
    slug: 'faker',
    displayName: 'Faker',
    source: 'leaguepedia',
    displayNameLocked: false,
    createdAtUtc: '2026-10-01T00:00:00Z',
    updatedAtUtc: '2026-10-01T00:00:00Z',
    removedAtUtc: null,
    trackedAccounts: 1,
    unresolvedAccounts: 0,
    links: 2,
    openConflicts: 0,
    ...overrides,
  }
}

const none = { personas: [], champions: [] }

describe('parseMatchId', () => {
  it('reads the platform_gameId shape, upper-cased', () => {
    expect(parseMatchId(' euw1_7012345678 ')).toBe('EUW1_7012345678')
    expect(parseMatchId('KR_7301234567')).toBe('KR_7301234567')
  })

  it('refuses anything else', () => {
    for (const term of ['EUW1', 'EUW1_', 'EUW1_12', '7012345678', 'EUW1-7012345678', 'Faker#KR1']) {
      expect(parseMatchId(term)).toBeNull()
    }
  })
})

describe('isEntityTerm', () => {
  it('needs two characters once trimmed', () => {
    expect(isEntityTerm('')).toBe(false)
    expect(isEntityTerm(' a ')).toBe(false)
    expect(isEntityTerm('ab')).toBe(true)
  })
})

describe('matchChampions', () => {
  it('ignores case, spaces and punctuation', () => {
    expect(matchChampions('kaisa', CHAMPIONS).map(c => c.name)).toEqual(['Kai\'Sa'])
    expect(matchChampions('lee sin', CHAMPIONS).map(c => c.name)).toEqual(['Lee Sin'])
    expect(matchChampions('LEESIN', CHAMPIONS).map(c => c.name)).toEqual(['Lee Sin'])
  })

  it('lists the names starting with the term first, then the rest by name', () => {
    expect(matchChampions('a', CHAMPIONS)).toEqual([])
    expect(matchChampions('ak', CHAMPIONS).map(c => c.name)).toEqual(['Akali'])
    expect(matchChampions('ah', [{ name: 'Shah' }, { name: 'Utah' }, { name: 'Ahri' }]).map(c => c.name)).toEqual(['Ahri', 'Shah', 'Utah'])
  })

  it('caps the list', () => {
    expect(matchChampions('ai', [{ name: 'Kai' }, { name: 'Taliyah' }, { name: 'Zaia' }], 2)).toHaveLength(2)
  })
})

describe('entitySearchGroups', () => {
  it('adds nothing under two characters', () => {
    expect(entitySearchGroups('', { personas: [persona()], champions: CHAMPIONS })).toEqual([])
    expect(entitySearchGroups('a', { personas: [persona()], champions: CHAMPIONS })).toEqual([])
  })

  it('turns a typed Riot ID into Trace and Add mains, ahead of everything', () => {
    const groups = entitySearchGroups(' Faker #KR1 ', { personas: [persona()], champions: [] })
    expect(groups.map(g => g.id)).toEqual(['entity-riot-id', 'entity-known-players'])
    const [trace, seed] = groups[0]!.items!
    expect(trace).toMatchObject({ label: 'Trace Faker#KR1', to: { path: '/accounts', query: { view: 'trace', riotId: 'Faker#KR1' } } })
    expect(seed).toMatchObject({ label: 'Add Faker#KR1 as a main', to: { path: '/accounts', query: { view: 'seed', riotId: 'Faker#KR1' } } })
    expect(groups.every(g => g.ignoreFilter)).toBe(true)
  })

  it('reads the hyphen form as a Riot ID after the known players', () => {
    const groups = entitySearchGroups('caps-g2', { personas: [persona({ id: 'p9', slug: 'caps-g2', displayName: 'Caps' })], champions: [] })
    expect(groups.map(g => g.id)).toEqual(['entity-known-players', 'entity-riot-id'])
    expect(groups[1]!.items![0]!.label).toBe('Trace caps#g2')
  })

  it('links each known player to its edit panel, removed ones flagged', () => {
    const personas = Array.from({ length: PERSONA_RESULT_LIMIT + 2 }, (_, i) => persona({ id: `p${i}`, slug: `s${i}`, displayName: `Name ${i}` }))
    personas[1] = persona({ id: 'gone', slug: 'gone', displayName: 'Gone', removedAtUtc: '2026-10-02T00:00:00Z' })
    const [group] = entitySearchGroups('name', { personas, champions: [] })
    expect(group!.label).toBe('Known players')
    expect(group!.items).toHaveLength(PERSONA_RESULT_LIMIT)
    expect(group!.items![0]).toMatchObject({ label: 'Name 0', description: 's0', to: { path: '/known-players', query: { player: 'p0' } } })
    expect(group!.items![1]!.description).toBe('gone · removed')
  })

  it('opens a match ID in the Data Quality slide-over', () => {
    const groups = entitySearchGroups('euw1_7012345678', none)
    expect(groups).toHaveLength(1)
    expect(groups[0]!.items![0]).toMatchObject({
      label: 'Inspect EUW1_7012345678',
      to: { path: '/data-quality', query: { match: 'EUW1_7012345678' } },
    })
  })

  it('filters the champions table on a champion name, with its icon when known', () => {
    const [group] = entitySearchGroups('ahri', { personas: [], champions: CHAMPIONS })
    expect(group!.id).toBe('entity-champions')
    expect(group!.items![0]).toMatchObject({ label: 'Ahri', avatar: { src: 'https://cdn/ahri.png' }, to: { path: '/champions', query: { search: 'Ahri' } } })
    const [kaisa] = entitySearchGroups('kai', { personas: [], champions: CHAMPIONS })
    expect(kaisa!.items![0]).toMatchObject({ label: 'Kai\'Sa', icon: 'i-lucide-swords' })
  })

  it('adds no group for a term nothing recognises', () => {
    expect(entitySearchGroups('zzzz', { personas: [], champions: CHAMPIONS })).toEqual([])
  })
})
