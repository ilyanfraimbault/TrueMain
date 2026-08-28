import { describe, expect, it } from 'vitest'
import { isUnsafeProxyPath } from '~~/server/utils/proxy-path'

// `server/utils/proxy-path.ts` is duplicated byte-for-byte in `admin/`; this suite
// exists on both sides so the copies cannot drift. The public proxy needs the guard
// as much as the ops one: it is reachable without any session at all.

describe('isUnsafeProxyPath', () => {
  it.each([
    '/champions',
    '/champions/103/builds?patch=25.16&elo=Emerald%2B',
    '/players/Sheiden%231234',
    '/matches/EUW1_7391827364',
    '',
  ])('forwards the ordinary path %s', (path) => {
    expect(isUnsafeProxyPath(path)).toBe(false)
  })

  it.each([
    '/../secrets',
    '/champions/../../etc/passwd',
    '/..',
    '//evil.example.com/steal',
    'https://evil.example.com/steal',
    '/champions\\..\\..\\etc',
  ])('rejects the plainly escaping path %s', (path) => {
    expect(isUnsafeProxyPath(path)).toBe(true)
  })

  it.each([
    '/%2e%2e%2fsecrets',
    '/champions%2F%2E%2E%2Fadmin',
    '/%2E%2E/secrets',
    '/champions%5C..%5Cetc',
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
    expect(isUnsafeProxyPath('/champions/%zz')).toBe(true)
    expect(isUnsafeProxyPath('/champions/100%')).toBe(true)
  })

  it('judges the path, not the query string', () => {
    // A query value is data: it cannot move the target URL, and rejecting it
    // would 400 a visitor whose search terms happen to look like a path.
    expect(isUnsafeProxyPath('/players/search?q=/../etc/passwd')).toBe(false)
    expect(isUnsafeProxyPath('/players/search?q=%2e%2e%2f')).toBe(false)
  })
})
