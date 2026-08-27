/**
 * Containment guard for the catch-all backend proxy.
 *
 * This file is deliberately duplicated between the two apps
 * (`web/server/utils/proxy-path.ts`, `admin/server/utils/proxy-path.ts`): they
 * are separately built and deployed Nuxt apps with no shared package. The
 * duplication is only safe if it stays in sync — **a change here is a change on
 * both sides**, and each app pins the behaviour with its own test so the copies
 * cannot drift silently.
 */

// WHATWG URL parsing — what `fetch` applies to the proxied target — folds `\`
// into `/` for http(s) URLs, so `..\` walks up exactly like `../`. Normalising
// once beats spelling every rule below twice.
function normalizeSeparators(path: string): string {
  return path.replaceAll('\\', '/')
}

// The three shapes that let a request path leave the configured backend:
//   `..` segments  → could walk above `base.pathname`
//   `//host/…`     → protocol-relative, points at a different host
//   `scheme://…`   → absolute URL, same problem
function escapesBackend(path: string): boolean {
  const candidate = normalizeSeparators(path)
  return /(^|\/)\.\.(\/|$)/.test(candidate)
    || /^\/\//.test(candidate)
    || /^\/?[a-z][a-z0-9+.-]*:\/\//i.test(candidate)
}

// An encoded separator still present *after* one decoding pass means the caller
// double-encoded it (`%252e%252f`). We stop at one pass rather than decoding to
// a fixed point: a second pass would be decoding data, not escapes, and would
// reject any path legitimately carrying a `%`. Spotting the leftover escape
// covers the same attack without that cost.
const ENCODED_SEPARATOR = /%(?:2e|2f|5c)/i

/**
 * Whether a proxy path must be rejected with a 400 instead of being forwarded.
 *
 * The patterns are tested against the **decoded** path as well as the raw one:
 * `%2e%2e%2f` is `../` to anything that decodes it, and testing the raw string
 * alone let it through all three patterns untouched (#1225).
 *
 * Only the path is inspected, never the query string: a query value that
 * happens to contain `../` is data — it cannot move the target URL — and
 * rejecting it would 400 a legitimate search.
 */
export function isUnsafeProxyPath(rawPath: string): boolean {
  const [pathname = ''] = rawPath.split('?')

  let decoded: string
  try {
    decoded = decodeURIComponent(pathname)
  }
  catch {
    // Malformed percent-escape (`%zz`, a trailing `%`). A path we cannot
    // normalise is a path we cannot clear, so a decode that throws is itself a
    // rejection — never an unhandled 500 out of the proxy.
    return true
  }

  return escapesBackend(pathname)
    || escapesBackend(decoded)
    || ENCODED_SEPARATOR.test(decoded)
}
