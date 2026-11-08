import { describe, expect, it } from 'vitest'
import type { RemoteRead } from '#common/utils/remote-read'
import { failed, isColdLoad, isNotFoundError, shownData, startLoading } from '#common/utils/remote-read'

/** What ofetch throws: an `Error` carrying the response status (#2476). */
function httpError(statusCode: number) {
  return Object.assign(new Error(`HTTP ${statusCode}`), { statusCode })
}

describe('remote read phase', () => {
  it('reads only a 404 as "not found"', () => {
    expect(isNotFoundError(httpError(404))).toBe(true)
    for (const status of [400, 429, 500, 502, 503]) {
      expect(isNotFoundError(httpError(status))).toBe(false)
    }
    expect(isNotFoundError(new TypeError('Failed to fetch'))).toBe(false)
  })

  it('turns a failure into notFound for a 404 and into an error keeping the last answer otherwise', () => {
    const ready: RemoteRead<string> = { kind: 'ready', data: 'page 3' }
    expect(failed(ready, httpError(404))).toEqual({ kind: 'notFound' })
    expect(failed(ready, httpError(502))).toEqual({ kind: 'error', error: httpError(502), previous: 'page 3' })
  })

  it('keeps the answer on screen through a refetch, and drops it for a fresh one', () => {
    const ready: RemoteRead<string> = { kind: 'ready', data: 'page 3' }
    expect(startLoading(ready)).toEqual({ kind: 'loading', previous: 'page 3' })
    expect(startLoading(ready, { fresh: true })).toEqual({ kind: 'loading', previous: null })
    // A retry after a failure keeps what the failure kept.
    const kept = failed(ready, httpError(500))
    expect(shownData(startLoading(kept))).toBe('page 3')
    // Nothing to keep after a 404.
    expect(startLoading<string>({ kind: 'notFound' })).toEqual({ kind: 'loading', previous: null })
  })

  it('is a cold load while waiting or loading with nothing to show', () => {
    expect(isColdLoad({ kind: 'waiting' })).toBe(true)
    expect(isColdLoad({ kind: 'loading', previous: null })).toBe(true)
    expect(isColdLoad({ kind: 'loading', previous: 'page 3' })).toBe(false)
    expect(isColdLoad({ kind: 'cleared' })).toBe(false)
    expect(isColdLoad({ kind: 'error', error: null, previous: null })).toBe(false)
  })
})
