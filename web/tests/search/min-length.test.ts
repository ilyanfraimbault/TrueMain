import { describe, expect, it } from 'vitest'
import { SEARCH_MIN_LENGTH, isQueryTooShort, searchNamePart } from '~~/app/composables/useTruemainSearch'

// Mirror of the backend's Search_returns_empty_200_for_too_short_or_missing_query:
// the frontend's "too short" guard measures the game-name part only, against
// SEARCH_MIN_LENGTH. SEARCH_MIN_LENGTH must stay in lock-step with
// MinQueryLength in SearchQueryService.cs — this asserts the value so a silent
// drift on the frontend side trips a test instead of shipping a mismatched hint.
describe('truemain search min-length guard', () => {
  it('pins SEARCH_MIN_LENGTH to the backend MinQueryLength value', () => {
    expect(SEARCH_MIN_LENGTH).toBe(2)
  })

  it('extracts the game-name part (before the tag) and trims it', () => {
    expect(searchNamePart('Phantasm')).toBe('Phantasm')
    expect(searchNamePart('  Phantasm  ')).toBe('Phantasm')
    expect(searchNamePart('Phantasm#EUW')).toBe('Phantasm')
    // Game names may contain hyphens; only '#' splits off the tag.
    expect(searchNamePart('Dark-Knight#EUW')).toBe('Dark-Knight')
  })

  it('measures the name part, so a tag never smuggles a short name past the floor', () => {
    expect(isQueryTooShort('')).toBe(true)
    expect(isQueryTooShort('a')).toBe(true)
    expect(isQueryTooShort('a#NA1')).toBe(true)
    expect(isQueryTooShort('  a  ')).toBe(true)
    expect(isQueryTooShort('ab')).toBe(false)
    expect(isQueryTooShort('ab#EUW')).toBe(false)
  })
})
