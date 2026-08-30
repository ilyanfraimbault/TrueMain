import { describe, expect, it } from 'vitest'
import { isUnsafeProxyPath } from '~~/server/utils/proxy-path'

// `server/utils/proxy-path.ts` is duplicated byte-for-byte in `web/`; this suite
// exists on both sides so the copies cannot drift. Handler-level tests (session,
// X-Ops-Key non-leak) are #1235 — this one pins the guard itself.

describe('isUnsafeProxyPath', () => {
  it.each([
    '/stats/overview',
    '/logs?level=Error&search=timeout',
    '/accounts/Sheiden%231234',
    '/data-quality/match/EUW1_7391827364',
    '',
  ])('forwards the ordinary path %s', (path) => {
    expect(isUnsafeProxyPath(path)).toBe(false)
  })

  it.each([
    '/../secrets',
    '/stats/../../etc/passwd',
    '/..',
    '//evil.example.com/steal',
    'https://evil.example.com/steal',
    '/stats\\..\\..\\etc',
  ])('rejects the plainly escaping path %s', (path) => {
    expect(isUnsafeProxyPath(path)).toBe(true)
  })

  it.each([
    '/%2e%2e%2fsecrets',
    '/stats%2F%2E%2E%2Fadmin',
    '/%2E%2E/secrets',
    '/stats%5C..%5Cetc',
  ])('rejects %s, which only escapes once decoded', (path) => {
    // The bug this suite was written for: the guard tested the raw string, so
    // every one of these sailed through all three patterns untouched (#1225).
    expect(isUnsafeProxyPath(path)).toBe(true)
  })

  it('rejects a double-encoded separator rather than trusting one decode pass', () => {
    expect(isUnsafeProxyPath('/%252e%252e%252fsecrets')).toBe(true)
  })

  it('rejects a path whose escapes cannot be decoded at all', () => {
    // A malformed escape makes `decodeURIComponent` throw. That has to end as a
    // rejection, not as an unhandled exception escaping the proxy handler.
    expect(isUnsafeProxyPath('/stats/%zz')).toBe(true)
    expect(isUnsafeProxyPath('/stats/100%')).toBe(true)
  })

  it('judges the path, not the query string', () => {
    // A query value is data: it cannot move the target URL, and rejecting it
    // would 400 an operator searching their logs for a literal path fragment.
    expect(isUnsafeProxyPath('/logs?search=/../etc/passwd')).toBe(false)
    expect(isUnsafeProxyPath('/logs?search=%2e%2e%2f')).toBe(false)
  })
})
