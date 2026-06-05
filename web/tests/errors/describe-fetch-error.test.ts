import { describe, expect, it } from 'vitest'
import { describeFetchError, fetchErrorStatus } from '~~/app/utils/errors'

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

  it('uses a generic request message for other 4xx', () => {
    expect(describeFetchError(fetchError(400))).toMatch(/could not be completed/i)
  })

  it('falls back to a connectivity message when there is no status', () => {
    expect(describeFetchError(new Error('network down'))).toMatch(/could not reach the server/i)
  })

  it('never leaks the raw ofetch message', () => {
    expect(describeFetchError(fetchError(500))).not.toContain('[GET]')
  })
})
