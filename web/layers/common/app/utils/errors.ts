/**
 * HTTP status carried by an ofetch failure, or `undefined` for a non-fetch
 * throw (network drop, abort, programmer error). ofetch raises a `FetchError`
 * that puts the response status on `statusCode`; the shape guard keeps a plain
 * `Error` from masquerading as an HTTP status.
 */
export function fetchErrorStatus(error: unknown): number | undefined {
  return error instanceof Error && 'statusCode' in error
    ? (error as { statusCode?: number }).statusCode
    : undefined
}

/**
 * Maps an HTTP status to the app's copy for it. Split out from
 * {@link describeFetchError} because `error.vue` receives a NuxtError that has
 * crossed the SSR payload — a *plain object*, not an `Error` instance — so the
 * shape guard below would read no status off it and every error page, 404
 * included, printed the connectivity line (#1661). A page holds the number
 * already; it has no fetch failure to inspect.
 */
export function describeHttpStatus(status: number | undefined): string {
  if (status === 429) {
    return 'Too many requests — please wait a moment and try again.'
  }
  if (status !== undefined && status >= 500) {
    return 'The server ran into a problem. Please try again shortly.'
  }
  // Before the generic 4xx line: a 404 is the one client error where "please
  // try again" is actively wrong, and it is what `error.vue` shows most often
  // (a typo'd champion slug, a dead shared link).
  if (status === 404) {
    return "We couldn't find what you were looking for."
  }
  if (status !== undefined && status >= 400) {
    return 'That request could not be completed. Please try again.'
  }
  // No status: the request never reached (or never heard back from) the server.
  return 'Could not reach the server. Check your connection and try again.'
}

/**
 * Maps a fetch failure to a short, human-friendly message. Centralised so that
 * `FetchErrorAlert` and `error.vue` always read the same line — and so a raw
 * ofetch string like `[GET] "/api/champions/1": 500` never reaches the UI.
 */
export function describeFetchError(error: unknown): string {
  return describeHttpStatus(fetchErrorStatus(error))
}
