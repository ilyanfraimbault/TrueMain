import { afterEach, describe, expect, it, vi } from 'vitest'
import { setAnalyticsOptOut } from '~/utils/analytics-opt-out'
import {
  clearVisitorId,
  getOrCreateVisitorId,
  VISITOR_ID_KEY,
  VISITOR_ID_LIFETIME_MONTHS,
  visitorIdExpiry,
} from '~/utils/analytics-visitor-id'

/**
 * The anonymous visitor id (#2149). The CNIL exemption's bounds are what is
 * pinned here: 13 months from creation, never extended by a later visit, gone
 * on opt-out, and no id at all rather than a fresh one per page load.
 */
const created = Date.UTC(2026, 9, 19, 12)
const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/

const throwingStorage = {
  getItem: () => {
    throw new Error('blocked')
  },
  setItem: () => {
    throw new Error('blocked')
  },
  removeItem: () => {
    throw new Error('blocked')
  },
} as unknown as Storage

function stored() {
  return JSON.parse(window.localStorage.getItem(VISITOR_ID_KEY)!) as { id: string, createdAt: number }
}

describe('analytics visitor id', () => {
  afterEach(() => {
    window.localStorage.clear()
    vi.unstubAllGlobals()
  })

  it('lives 13 months from its creation', () => {
    expect(VISITOR_ID_LIFETIME_MONTHS).toBe(13)
    expect(visitorIdExpiry(created)).toBe(Date.UTC(2027, 10, 19, 12))
  })

  it('creates a random id once, stored with its creation date', () => {
    const id = getOrCreateVisitorId(window.localStorage, created)

    expect(id).toMatch(UUID)
    expect(stored()).toEqual({ id, createdAt: created })
  })

  it('reuses the id without extending its lifetime', () => {
    const id = getOrCreateVisitorId(window.localStorage, created)
    const setItem = vi.fn()
    const watched = {
      getItem: (key: string) => window.localStorage.getItem(key),
      setItem,
    } as unknown as Storage

    const later = visitorIdExpiry(created) - 1
    expect(getOrCreateVisitorId(watched, later)).toBe(id)
    expect(setItem).not.toHaveBeenCalled()
    expect(stored().createdAt).toBe(created)
  })

  it('replaces the id once expired', () => {
    const id = getOrCreateVisitorId(window.localStorage, created)
    const expired = visitorIdExpiry(created)

    const renewed = getOrCreateVisitorId(window.localStorage, expired)
    expect(renewed).toMatch(UUID)
    expect(renewed).not.toBe(id)
    expect(stored()).toEqual({ id: renewed, createdAt: expired })
  })

  it('replaces an id dated in the future or unreadable', () => {
    window.localStorage.setItem(VISITOR_ID_KEY, JSON.stringify({ id: 'old', createdAt: created + 1 }))
    expect(getOrCreateVisitorId(window.localStorage, created)).not.toBe('old')

    window.localStorage.setItem(VISITOR_ID_KEY, '{not json')
    expect(getOrCreateVisitorId(window.localStorage, created)).toMatch(UUID)

    window.localStorage.setItem(VISITOR_ID_KEY, JSON.stringify({ id: '', createdAt: created }))
    expect(getOrCreateVisitorId(window.localStorage, created)).toMatch(UUID)
  })

  it('falls back to getRandomValues outside a secure context', () => {
    const { getRandomValues } = globalThis.crypto
    vi.stubGlobal('crypto', { getRandomValues: getRandomValues.bind(globalThis.crypto) })

    expect(getOrCreateVisitorId(window.localStorage, created)).toMatch(UUID)
  })

  it('gives no id when no random source exists', () => {
    vi.stubGlobal('crypto', undefined)

    expect(getOrCreateVisitorId(window.localStorage, created)).toBeNull()
    expect(window.localStorage.getItem(VISITOR_ID_KEY)).toBeNull()
  })

  it('gives no id when the device cannot keep it', () => {
    expect(getOrCreateVisitorId(throwingStorage, created)).toBeNull()
    expect(getOrCreateVisitorId(null, created)).toBeNull()

    const forgetful = { getItem: () => null, setItem: () => {} } as unknown as Storage
    expect(getOrCreateVisitorId(forgetful, created)).toBeNull()
  })

  it('reads the browser storage by default', () => {
    const id = getOrCreateVisitorId()

    expect(id).toMatch(UUID)
    expect(getOrCreateVisitorId()).toBe(id)
  })

  it('is removed by clearing or by an opt-out', () => {
    getOrCreateVisitorId()
    clearVisitorId()
    expect(window.localStorage.getItem(VISITOR_ID_KEY)).toBeNull()
    expect(() => clearVisitorId(throwingStorage)).not.toThrow()

    getOrCreateVisitorId()
    setAnalyticsOptOut(true)
    expect(window.localStorage.getItem(VISITOR_ID_KEY)).toBeNull()
  })
})
