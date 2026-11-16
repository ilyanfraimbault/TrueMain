import type { H3Event } from 'h3'

/**
 * The one way the site's server routes read the API or the static-data routes
 * (#1557): only an absence is cached, and the fan-out is attributed to the
 * visitor who caused it.
 *
 * - **A 404 describes the thing asked for** — the champion has no aggregate on
 *   that slice, the Riot ID is unknown — and is as true in five minutes as now:
 *   {@link readOrAbsent} answers it as `null`, which a cached loader may store.
 * - **A 429, a 5xx or an unreachable upstream describes the moment**: it is
 *   rethrown, so `defineCachedFunction` stores nothing, and
 *   {@link defineCachedRead} answers that one call with its fallback. One
 *   throttled request must never blank a card or a paragraph for an hour.
 * - **The visitor's `X-Forwarded-For` is the only header forwarded.** These are
 *   in-process calls through the `/api` proxy: without it the API keys every
 *   call on the web container, one rate-limit bucket for the whole site. The
 *   rest of the request has no business in an answer shared through a cache.
 */

/** The status of a failed upstream call, when it got as far as an HTTP answer. */
export function upstreamStatus(error: unknown): number | undefined {
  const failure = error as { statusCode?: unknown, response?: { status?: unknown } } | null
  const status = failure?.statusCode ?? failure?.response?.status
  return typeof status === 'number' ? status : undefined
}

/** The visitor's address, to pass to a cached loader as an argument its key ignores. */
export function visitorAddress(event: H3Event): string | undefined {
  return getRequestHeader(event, 'x-forwarded-for')
}

export interface UpstreamRead {
  query?: Record<string, string | number | undefined>
  /** From {@link visitorAddress}. */
  forwardedFor?: string
}

/** `GET path`: the body, `null` on a 404, and a throw on any other failure. */
export async function readOrAbsent<T>(path: string, read: UpstreamRead = {}): Promise<T | null> {
  try {
    return await $fetch<T>(path, {
      query: read.query,
      headers: read.forwardedFor ? { 'x-forwarded-for': read.forwardedFor } : undefined,
    })
  }
  catch (error) {
    if (upstreamStatus(error) === 404) return null
    throw error
  }
}

interface CachedReadOptions<A extends unknown[]> {
  name: string
  maxAge: number
  getKey: (...args: A) => string
}

/**
 * A cached loader that stores answers and never failures: when `load` throws
 * (anything {@link readOrAbsent} did not turn into `null`), nothing is stored
 * and this call answers `fallback(...args)`, so the next one retries.
 */
export function defineCachedRead<T, A extends unknown[]>(
  load: (...args: A) => Promise<T>,
  options: CachedReadOptions<A>,
  fallback: (...args: A) => T,
): (...args: A) => Promise<T> {
  const cached = defineCachedFunction<T, A>(load, options)
  return async (...args: A) => {
    try {
      return await cached(...args)
    }
    catch {
      return fallback(...args)
    }
  }
}
