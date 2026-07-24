import { describe, expect, it } from 'vitest'
import { hasSummary } from '~~/shared/utils/process-summary'

describe('hasSummary', () => {
  it('treats an absent summary as empty', () => {
    expect(hasSummary(null)).toBe(false)
    expect(hasSummary(undefined)).toBe(false)
  })

  it('treats an empty array as empty', () => {
    expect(hasSummary([])).toBe(false)
  })

  it('treats an empty object as empty', () => {
    expect(hasSummary({})).toBe(false)
  })

  it('treats the empty string as empty', () => {
    expect(hasSummary('')).toBe(false)
  })

  it('reports content for a populated object', () => {
    expect(hasSummary({ matchesIngested: 120 })).toBe(true)
    // A key present with a falsy value is still content to display.
    expect(hasSummary({ matchesIngested: 0 })).toBe(true)
  })

  it('reports content for a populated array', () => {
    expect(hasSummary([{ platform: 'EUW1', ingested: 12 }])).toBe(true)
    expect(hasSummary(['EUW1'])).toBe(true)
    // An array whose single element is falsy is still a non-empty payload.
    expect(hasSummary([0])).toBe(true)
  })

  it('reports content for a non-empty scalar summary', () => {
    expect(hasSummary('done')).toBe(true)
    expect(hasSummary(0)).toBe(true)
    expect(hasSummary(42)).toBe(true)
    expect(hasSummary(false)).toBe(true)
  })
})
