import { describe, expect, it } from 'vitest'
import { describeFetchError, describeHttpStatus, fetchErrorStatus } from '~~/app/utils/errors'

/** Build an ofetch-style FetchError: a real Error carrying `statusCode`. */
function fetchError(statusCode: number): Error {
  return Object.assign(new Error(`[GET] "/api/x": ${statusCode}`), { statusCode })
}

describe('fetchErrorStatus', () => {
  it('reads statusCode off a FetchError-shaped Error', () => {
    expect(fetchErrorStatus(fetchError(503))).toBe(503)
  })

  it('returns undefined for a plain Error with no status', () => {
    expect(fetchErrorStatus(new Error('boom'))).toBeUndefined()
  })

  it('returns undefined for non-Error throws', () => {
    expect(fetchErrorStatus('nope')).toBeUndefined()
    expect(fetchErrorStatus(null)).toBeUndefined()
    expect(fetchErrorStatus({ statusCode: 500 })).toBeUndefined()
  })
})

describe('describeFetchError', () => {
  it('calls out rate limiting on 429', () => {
    expect(describeFetchError(fetchError(429))).toMatch(/too many requests/i)
  })

  it('blames the server on any 5xx', () => {
    expect(describeFetchError(fetchError(500))).toMatch(/server ran into a problem/i)
    expect(describeFetchError(fetchError(503))).toMatch(/server ran into a problem/i)
  })

  // 404 is the one client error where "please try again" is wrong advice, and
  // it is the status `error.vue` shows most often (#1661).
  it('tells a 404 apart from the generic 4xx line', () => {
    expect(describeFetchError(fetchError(404))).toMatch(/couldn't find/i)
    expect(describeFetchError(fetchError(404))).not.toMatch(/try again/i)
  })

  it('uses a generic request message for other 4xx', () => {
    expect(describeFetchError(fetchError(400))).toMatch(/could not be completed/i)
    expect(describeFetchError(fetchError(403))).toMatch(/could not be completed/i)
  })

  it('falls back to a connectivity message when there is no status', () => {
    expect(describeFetchError(new Error('network down'))).toMatch(/could not reach the server/i)
  })

  it('never leaks the raw ofetch message', () => {
    expect(describeFetchError(fetchError(500))).not.toContain('[GET]')
  })
})

describe('describeHttpStatus', () => {
  // The regression that split this function out of describeFetchError (#1661):
  // `error.vue` is handed a NuxtError that crossed the SSR payload, so it is a
  // plain object and `instanceof Error` is false. Routed through the fetch
  // helper, every error page — 404 included — printed the connectivity line.
  it('reads a status the fetch helper cannot see on a payload-serialised error', () => {
    const serialised = { statusCode: 404, statusMessage: 'Page not found: /nope' }
    expect(fetchErrorStatus(serialised)).toBeUndefined()
    expect(describeHttpStatus(serialised.statusCode)).toMatch(/couldn't find/i)
  })

  it('agrees with describeFetchError on a real FetchError', () => {
    for (const status of [404, 429, 500, 503, 400]) {
      expect(describeHttpStatus(status)).toBe(describeFetchError(fetchError(status)))
    }
  })
})
