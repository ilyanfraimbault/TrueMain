/**
 * Whether a path the `/api` proxy would forward lands on the API's `/internal`
 * surface (#1556). That surface is for server-side callers holding their own key —
 * this app's log forwarder among them — and is never reachable by a visitor
 * through the proxy, key or not.
 *
 * Decoded, separator-normalised, dot-segment-stripped and lower-cased before the
 * test: ASP.NET routes case-insensitively on the decoded path, and `fetch` resolves
 * `/./`, so `/INTERNAL/logs`, `/%69nternal/logs` and `/./internal/logs` all reach
 * the same endpoint as `/internal/logs`.
 */
export function isInternalApiPath(rawPath: string): boolean {
  const [pathname = ''] = rawPath.split('?')
  let decoded: string
  try {
    decoded = decodeURIComponent(pathname)
  }
  catch {
    return true
  }
  const normalized = decoded
    .replaceAll('\\', '/')
    .replace(/\/{2,}/g, '/')
    .replace(/\/\.(?=\/|$)/g, '')
    .toLowerCase()
  return normalized === '/internal' || normalized.startsWith('/internal/')
}
