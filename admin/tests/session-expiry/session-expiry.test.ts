import { describe, expect, it } from 'vitest'
import { isSessionExpiry, LOGIN_PATH, loginRedirectTarget } from '~/utils/session-expiry'

// The rule the global `$fetch` interceptor (`app/plugins/session-expiry.client.ts`)
// is built on. What matters here is the *narrowness* of the match: too wide and a
// mistyped password logs the operator out mid-login, too narrow and an expired
// session keeps painting the dashboard red as if the backend were down (#1225).

describe('isSessionExpiry', () => {
  it.each([
    '/api/ops/stats/overview',
    '/api/ops/logs?level=Error&page=2',
    '/api/ops/accounts/seed/42',
    '/api/static/champions',
  ])('treats a 401 on %s as an expired session', (url) => {
    expect(isSessionExpiry(url, 401)).toBe(true)
  })

  it('recognises the session-gated proxies through an absolute URL', () => {
    // ofetch hands the interceptor whatever the caller passed; both forms occur.
    expect(isSessionExpiry('https://admin.truemain.lol/api/ops/health', 401)).toBe(true)
  })

  it('does not treat a rejected login as an expiry', () => {
    // `/api/auth/login` answers 401 for wrong credentials. Clearing the session
    // there would log out an operator who simply mistyped, mid-login.
    expect(isSessionExpiry('/api/auth/login', 401)).toBe(false)
  })

  it('does not fire on a sibling route that merely shares a prefix', () => {
    expect(isSessionExpiry('/api/opsomething', 401)).toBe(false)
    expect(isSessionExpiry('/api/staticky', 401)).toBe(false)
  })

  it.each([200, 400, 403, 404, 500, 502])('leaves a %i on the ops proxy alone', (status) => {
    // Only 401 means "no session". A 403 or a 500 is a real failure the panel
    // must keep showing, and logging out on it would hide the outage.
    expect(isSessionExpiry('/api/ops/stats/overview', status)).toBe(false)
  })

  it('does not throw on a URL it cannot parse', () => {
    expect(isSessionExpiry('::not a url::', 401)).toBe(false)
  })
})

describe('loginRedirectTarget', () => {
  it('sends an expired session to the login page', () => {
    expect(loginRedirectTarget('/logs')).toBe(LOGIN_PATH)
  })

  it('returns nothing when the login page is already showing', () => {
    // The guard against a redirect loop: a 401 raised from /login — or a second
    // one landing after the first redirect — must not navigate again.
    expect(loginRedirectTarget(LOGIN_PATH)).toBeNull()
  })
})
