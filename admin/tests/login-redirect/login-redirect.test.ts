import { describe, expect, it } from 'vitest'
import { DEFAULT_REDIRECT, LOGIN_PATH, loginLocation, safeRedirectPath } from '~~/shared/utils/login-redirect'

// `?redirect=` is read from the URL, so anyone can craft a login link carrying it.
// The sanitiser is the only thing between that value and `navigateTo`: it must keep
// every deep link of the portal intact and refuse anything that leaves the origin.

describe('safeRedirectPath', () => {
  it.each([
    '/known-players?status=homonyms',
    '/known-players?status=open&conflict=42',
    '/accounts?view=trace',
    '/logs?view=crashes',
    '/health',
    '/',
  ])('keeps the same-origin path %s', (path) => {
    expect(safeRedirectPath(path)).toBe(path)
  })

  it('keeps a hash fragment', () => {
    expect(safeRedirectPath('/processes?tab=riot#budget')).toBe('/processes?tab=riot#budget')
  })

  it.each([
    ['a protocol-relative URL', '//evil.com'],
    ['a protocol-relative URL with a path', '//evil.com/known-players'],
    ['an absolute URL', 'https://evil.com'],
    ['an absolute URL to this very path', 'https://evil.com/known-players'],
    ['a backslash the parser turns into //', '/\\evil.com'],
    ['a backslash later in the path', '/\\/evil.com'],
    ['a tab the parser strips', '/\t/evil.com'],
    ['a newline the parser strips', '/\n/evil.com'],
    ['a still-encoded protocol-relative URL', '%2F%2Fevil.com'],
    ['a script URL', 'javascript:alert(1)'],
    ['a data URL', 'data:text/html,<script>alert(1)</script>'],
    ['a relative path without a leading slash', 'known-players'],
    ['an empty string', ''],
    ['the login page', '/login'],
    ['the login page with a trailing slash', '/login/'],
    ['the login page carrying its own redirect', '/login?redirect=/health'],
    ['a nested redirect to another origin', '/login?redirect=//evil.com'],
  ])('falls back to / for %s', (_label, candidate) => {
    expect(safeRedirectPath(candidate)).toBe(DEFAULT_REDIRECT)
  })

  it.each([undefined, null, 42, ['/health'], {}])('falls back to / for the non-string %j', (candidate) => {
    // Vue Router hands over `string | null | (string | null)[]` — a repeated
    // `?redirect=` arrives as an array, which is refused rather than guessed at.
    expect(safeRedirectPath(candidate)).toBe(DEFAULT_REDIRECT)
  })

  it('keeps an encoded slash inside the path on this origin', () => {
    // Percent-encoded, `%2F` is a path character, not a separator: the URL stays here.
    expect(safeRedirectPath('/%2F/evil.com')).toBe('/%2F/evil.com')
  })
})

describe('loginLocation', () => {
  it('remembers the page the operator was on', () => {
    expect(loginLocation('/known-players?player=7')).toEqual({
      path: LOGIN_PATH,
      query: { redirect: '/known-players?player=7' },
    })
  })

  it('adds no redirect for the dashboard root', () => {
    expect(loginLocation('/')).toEqual({ path: LOGIN_PATH })
  })

  it('adds no redirect a sign-in would refuse anyway', () => {
    expect(loginLocation('//evil.com')).toEqual({ path: LOGIN_PATH })
  })
})
