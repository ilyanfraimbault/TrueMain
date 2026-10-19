import { afterEach, describe, expect, it } from 'vitest'
import { ANALYTICS_OPT_OUT_KEY, isAnalyticsOptedOut, setAnalyticsOptOut } from '~/utils/analytics-opt-out'

/**
 * The analytics opt-out (#2089). The key is pinned on purpose: it is Umami's
 * tracker that reads it, so renaming it would silently stop honouring every
 * refusal already recorded.
 */
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

describe('analytics opt-out', () => {
  afterEach(() => {
    window.localStorage.clear()
  })

  it('uses the flag the Umami tracker reads', () => {
    expect(ANALYTICS_OPT_OUT_KEY).toBe('umami.disabled')
  })

  it('is opted in by default', () => {
    expect(isAnalyticsOptedOut()).toBe(false)
  })

  it('records and lifts a refusal', () => {
    expect(setAnalyticsOptOut(true)).toBe(true)
    expect(window.localStorage.getItem('umami.disabled')).toBeTruthy()
    expect(isAnalyticsOptedOut()).toBe(true)

    expect(setAnalyticsOptOut(false)).toBe(false)
    expect(window.localStorage.getItem('umami.disabled')).toBeNull()
  })

  it('reports no refusal when storage is blocked', () => {
    expect(isAnalyticsOptedOut(throwingStorage)).toBe(false)
    expect(setAnalyticsOptOut(true, throwingStorage)).toBe(false)
  })
})
