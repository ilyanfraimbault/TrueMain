export const LOGIN_PATH = '/login'

/**
 * Proxy prefixes whose 401 can only mean "the operator session is gone".
 *
 * Both are gated by `requireUserSession` server-side (`server/api/ops/[...path].ts`,
 * `server/api/static/champions.get.ts`), so they never answer 401 for any other
 * reason. The list is deliberately narrow rather than "any 401": `/api/auth/login`
 * answers 401 for *wrong credentials*, and treating that as an expiry would clear
 * the session of an operator who simply mistyped, mid-login.
 */
const SESSION_GATED_PREFIXES = ['/api/ops', '/api/static']

// The request URL reaching an interceptor is normally relative (`/api/ops/logs`),
// but ofetch also hands over absolute ones. The base is a parsing scaffold only —
// it is ignored whenever the URL already carries an origin.
function pathnameOf(url: string): string {
  try {
    return new URL(url, 'http://localhost').pathname
  }
  catch {
    return ''
  }
}

/**
 * Whether a failed response means the operator's session expired, rather than
 * the request itself being refused.
 *
 * Matching on a path boundary, not a bare `startsWith`, so a future
 * `/api/opsomething` route cannot be mistaken for the ops proxy.
 */
export function isSessionExpiry(url: string, status: number): boolean {
  if (status !== 401) {
    return false
  }
  const pathname = pathnameOf(url)
  return SESSION_GATED_PREFIXES.some(
    prefix => pathname === prefix || pathname.startsWith(`${prefix}/`),
  )
}

/**
 * Where to send an operator whose session just expired — `null` when they are
 * already on the login page, so a 401 fired from `/login` (or a second one
 * arriving after the redirect) cannot bounce the router against itself.
 */
export function loginRedirectTarget(currentPath: string): string | null {
  return currentPath === LOGIN_PATH ? null : LOGIN_PATH
}
