import { describe, expect, it } from 'vitest'
import { DEFAULT_LOG_LEVEL, LOG_LEVELS, parseLevelQuery } from '~/utils/log-level'

// The Logs page opens on Warning and above (#1415), so `parseLevelQuery` decides
// what the operator sees on first paint: an explicit `?level=` must win — the
// `all` escape hatch included — and anything unusable must fall back to the
// errors-first default rather than silently widening the view.

describe('parseLevelQuery', () => {
  it('defaults to Warning when the query param is absent', () => {
    expect(parseLevelQuery(undefined)).toBe(DEFAULT_LOG_LEVEL)
    expect(DEFAULT_LOG_LEVEL).toBe('Warning')
  })

  it('honours the "all" sentinel', () => {
    expect(parseLevelQuery('all')).toBe('all')
    expect(parseLevelQuery('ALL')).toBe('all')
  })

  it.each(LOG_LEVELS)('honours an explicit %s deep link', (level) => {
    expect(parseLevelQuery(level)).toBe(level)
  })

  it('matches level names case-insensitively', () => {
    expect(parseLevelQuery('information')).toBe('Information')
    expect(parseLevelQuery(' error ')).toBe('Error')
  })

  it('falls back to the default on an unknown or non-string value', () => {
    expect(parseLevelQuery('nope')).toBe(DEFAULT_LOG_LEVEL)
    expect(parseLevelQuery('')).toBe(DEFAULT_LOG_LEVEL)
    expect(parseLevelQuery(null)).toBe(DEFAULT_LOG_LEVEL)
    expect(parseLevelQuery(42)).toBe(DEFAULT_LOG_LEVEL)
  })

  it('reads the first value of a repeated query param', () => {
    expect(parseLevelQuery(['Critical', 'Trace'])).toBe('Critical')
  })
})
