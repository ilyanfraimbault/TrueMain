import { describe, expect, it } from 'vitest'
import { describeFetchError } from '#common/utils/errors'
import { isNotFoundError } from '#common/utils/remote-read'
import { asFetchError } from '../app/utils/shell-error'

// Inside Tauri a failed read rejects with the shell's string (`api.rs`), which the
// shared pages cannot read a status from (#2476): `useApiFetch` hands them an
// ofetch-shaped error instead.
describe('asFetchError', () => {
  it('reads a 404 from the shell as "not found"', () => {
    const error = asFetchError('TrueMain answered 404 Not Found for /players/nobody')

    expect(error).toBeInstanceOf(Error)
    expect(isNotFoundError(error)).toBe(true)
  })

  it('reads any other status as an error with its own copy', () => {
    const error = asFetchError('TrueMain answered 502 Bad Gateway for /truemains/Found-EUW/profile')

    expect(isNotFoundError(error)).toBe(false)
    expect((error as { statusCode: number }).statusCode).toBe(502)
    expect(describeFetchError(error)).toBe(describeFetchError(Object.assign(new Error('x'), { statusCode: 502 })))
  })

  it('leaves a read that never reached TrueMain an error, never "not found"', () => {
    const offline = 'could not reach TrueMain: connection refused'

    expect(asFetchError(offline)).toBe(offline)
    expect(isNotFoundError(asFetchError(offline))).toBe(false)
  })

  it('passes an ofetch error through untouched', () => {
    const error = Object.assign(new Error('Not Found'), { statusCode: 404 })

    expect(asFetchError(error)).toBe(error)
  })
})
