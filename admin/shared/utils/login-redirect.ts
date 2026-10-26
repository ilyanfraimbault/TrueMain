/**
 * The page a signed-out operator was heading to, carried through `/login` as
 * `?redirect=<path+query>` (#2289).
 *
 * The value comes from the URL, so anyone can craft it: `safeRedirectPath` is
 * the single gate between that query parameter and `navigateTo`, which would
 * happily leave the origin for `//evil.com` or `https://evil.com`. Only a
 * same-origin relative path survives; everything else falls back to `/`.
 */

export const LOGIN_PATH = '/login'
export const DEFAULT_REDIRECT = '/'

// A parsing scaffold only: a candidate that resolves to any other origin
// (protocol-relative, absolute, backslash tricks the URL parser normalises to
// `//`) is exactly what the check rejects.
const BASE = 'https://admin.invalid'

// Whitespace and control characters are stripped or reinterpreted by URL
// parsers (`/\t/evil.com` parses as `//evil.com`), so they are refused outright.
// eslint-disable-next-line no-control-regex
const UNSAFE_CHARS = /[\u0000- \u007F\\]/

function isLoginPath(pathname: string): boolean {
  return pathname === LOGIN_PATH || pathname === `${LOGIN_PATH}/`
}

/**
 * The redirect target to navigate to after signing in: the candidate itself,
 * normalised, when it is a same-origin relative path that is not the login page,
 * else `/`. Accepts the raw query value (Vue Router hands over `string`,
 * `string[]`, `null` or `undefined`).
 */
export function safeRedirectPath(candidate: unknown): string {
  if (typeof candidate !== 'string') {
    return DEFAULT_REDIRECT
  }
  // A single leading `/`, then anything but a second `/` or a `\`.
  if (!candidate.startsWith('/') || candidate.startsWith('//') || UNSAFE_CHARS.test(candidate)) {
    return DEFAULT_REDIRECT
  }
  let url: URL
  try {
    url = new URL(candidate, BASE)
  }
  catch {
    return DEFAULT_REDIRECT
  }
  if (url.origin !== BASE || isLoginPath(url.pathname)) {
    return DEFAULT_REDIRECT
  }
  return `${url.pathname}${url.search}${url.hash}`
}

/** Where to send a signed-out operator: `/login`, remembering the page they were on. */
export interface LoginLocation {
  path: string
  query?: { redirect: string }
}

/**
 * The login route for an operator who was on `fullPath` (path + query + hash).
 * The redirect is left out when it would add nothing — the dashboard root, or a
 * path the sanitiser would refuse anyway.
 */
export function loginLocation(fullPath: string): LoginLocation {
  const redirect = safeRedirectPath(fullPath)
  return redirect === DEFAULT_REDIRECT
    ? { path: LOGIN_PATH }
    : { path: LOGIN_PATH, query: { redirect } }
}
