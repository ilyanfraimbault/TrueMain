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
 * Maps a fetch failure to a short, human-friendly message. Centralised so the
 * inline error state and the error toast always read the same line — and so a
 * raw ofetch string like `[GET] "/api/champions/1": 500` never reaches the UI.
 */
export function describeFetchError(error: unknown): string {
  const status = fetchErrorStatus(error)
  if (status === 429) {
    return 'Too many requests — please wait a moment and try again.'
  }
  if (status !== undefined && status >= 500) {
    return 'The server ran into a problem. Please try again shortly.'
  }
  if (status !== undefined && status >= 400) {
    return 'That request could not be completed. Please try again.'
  }
  // No status: the request never reached (or never heard back from) the server.
  return 'Could not reach the server. Check your connection and try again.'
}
