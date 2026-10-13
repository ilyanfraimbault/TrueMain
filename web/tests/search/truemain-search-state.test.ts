import { describe, expect, it } from 'vitest'
import type { SearchResult } from '#shared/types/search'
import { TRUEMAIN_SEARCH_MESSAGES, truemainSearchAnnouncement, truemainSearchState } from '#common/utils/truemain-search-state'

const result = (gameName: string): SearchResult => ({
  identity: { gameName, tagLine: 'EUW', profileIconId: 1, platformId: 'EUW1', summonerLevel: 30 },
  region: 'europe',
  ranked: null,
  topChampionIds: [],
  positions: null,
})

describe('truemainSearchState', () => {
  const base = { term: 'Faker', tooShort: false, status: 'success' as const, results: [] }

  it('stays idle on an empty box', () => {
    expect(truemainSearchState({ ...base, term: '   ' })).toEqual({ kind: 'idle' })
  })

  it('asks for more letters before anything else', () => {
    expect(truemainSearchState({ ...base, term: 'a', tooShort: true, status: 'error' })).toEqual({ kind: 'tooShort' })
  })

  it('keeps the previous rows while a newer query is pending', () => {
    const rows = [result('Faker')]
    expect(truemainSearchState({ ...base, status: 'pending', results: rows })).toEqual({ kind: 'refetching', results: rows })
    expect(truemainSearchState({ ...base, status: 'pending' })).toEqual({ kind: 'searching' })
  })

  it('tells an empty answer from a failed one', () => {
    expect(truemainSearchState(base)).toEqual({ kind: 'empty' })
    expect(truemainSearchState({ ...base, status: 'error' })).toEqual({ kind: 'error' })
  })
})

describe('truemainSearchAnnouncement', () => {
  it('announces what the palette shows', () => {
    expect(truemainSearchAnnouncement({ kind: 'idle' }, 'failed', 'none')).toBe('')
    expect(truemainSearchAnnouncement({ kind: 'tooShort' }, 'failed', 'none')).toBe(TRUEMAIN_SEARCH_MESSAGES.tooShort)
    expect(truemainSearchAnnouncement({ kind: 'refetching', results: [] }, 'failed', 'none')).toBe(TRUEMAIN_SEARCH_MESSAGES.searching)
    expect(truemainSearchAnnouncement({ kind: 'error' }, 'failed', 'none')).toBe('failed')
    expect(truemainSearchAnnouncement({ kind: 'empty' }, 'failed', 'none')).toBe('none')
  })

  it('counts the results', () => {
    expect(truemainSearchAnnouncement({ kind: 'results', results: [result('A')] }, '', '')).toBe('1 truemain found.')
    expect(truemainSearchAnnouncement({ kind: 'results', results: [result('A'), result('B')] }, '', '')).toBe('2 truemains found.')
  })
})
