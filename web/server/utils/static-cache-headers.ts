/**
 * Browser caching for the static game data under `/api/static/*` (#1584).
 *
 * Those handlers cache their upstream (Data Dragon, CommunityDragon) for an hour
 * on the server, but answered with no `Cache-Control`, so the browser fetched them
 * again on every reload — about 700 KB on a champion page — and the page's icons
 * waited for them. The client-side cache (`app/utils/static-cache.ts`) only lives
 * as long as the page.
 *
 * Same hour as the server, so a patch release reaches a visitor no later than it
 * reaches the server cache; `stale-while-revalidate` lets a reload past the hour
 * paint from the copy it has while the browser refreshes it.
 */
export const STATIC_DATA_CACHE_CONTROL = 'public, max-age=3600, stale-while-revalidate=86400'

/**
 * The `Cache-Control` to add to a response, or null. Only successful static-data
 * answers are cacheable: an error must never be kept for an hour in a visitor's
 * browser.
 */
export function staticDataCacheControl(path: string | undefined, status: number): string | null {
  if (!path || status !== 200) return null
  return /^\/api\/static\//.test(path) ? STATIC_DATA_CACHE_CONTROL : null
}
