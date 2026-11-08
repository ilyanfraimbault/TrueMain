/**
 * The shell reports an HTTP status as a string — `TrueMain answered 404 Not Found
 * for /players/x` (`api.rs`) — while the shared pages read a status the way ofetch
 * carries it: `statusCode` on an `Error` (`fetchErrorStatus`). Handing them that
 * shape keeps a 404 "not found" in the app too (#2476), and lets the error copy
 * name the right cause. Any other rejection — "could not reach TrueMain" included —
 * is passed through untouched: no status, so an error, never "not found".
 */
export function asFetchError(error: unknown): unknown {
  if (typeof error !== 'string') return error
  const status = /^TrueMain answered (\d{3})\b/.exec(error)?.[1]
  return status ? Object.assign(new Error(error), { statusCode: Number(status) }) : error
}
