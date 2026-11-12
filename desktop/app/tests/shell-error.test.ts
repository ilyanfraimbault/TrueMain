import { describe, expect, it } from 'vitest'
import { describeFetchError } from '#common/utils/errors'
import { isNotFoundError } from '#common/utils/remote-read'
import { asFetchError, isSuperseded } from '../app/utils/shell-error'

// Inside Tauri a failed read rejects with the shell's `ApiError`; the shared
// pages read a status off an ofetch-shaped error (#2476).
describe('asFetchError', () => {
  it('reads a 404 from the shell as "not found"', () => {
    const error = asFetchError({ kind: 'answered', status: 404, message: 'TrueMain answered 404 Not Found for /players/nobody' })

    expect(error).toBeInstanceOf(Error)
    expect(isNotFoundError(error)).toBe(true)
    expect(String(error)).toBe('TrueMain answered 404 Not Found for /players/nobody')
  })

  it('reads any other status as an error with its own copy', () => {
    const error = asFetchError({ kind: 'answered', status: 502, message: 'TrueMain answered 502 Bad Gateway' })

    expect(isNotFoundError(error)).toBe(false)
    expect((error as { statusCode: number }).statusCode).toBe(502)
    expect(describeFetchError(error)).toBe(describeFetchError(Object.assign(new Error('x'), { statusCode: 502 })))
  })

  it('leaves a read that never reached TrueMain an error, never "not found"', () => {
    const offline = asFetchError({ kind: 'unreachable', message: 'could not reach TrueMain: connection refused' })

    expect(offline).toBe('could not reach TrueMain: connection refused')
    expect(isNotFoundError(offline)).toBe(false)
  })

  it('keeps a superseded read recognisable', () => {
    expect(isSuperseded(asFetchError({ kind: 'superseded' }))).toBe(true)
    expect(isSuperseded('superseded')).toBe(false)
  })

  it('passes an ofetch error through untouched', () => {
    const error = Object.assign(new Error('Not Found'), { statusCode: 404 })

    expect(asFetchError(error)).toBe(error)
  })
})
