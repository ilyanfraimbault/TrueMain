// Explicit imports rather than Nuxt auto-imports: this module is unit tested
// outside the Nuxt context (`tests/ops-cache`).
import { shallowReactive } from 'vue'

/**
 * The client-side cache behind instant navigation in the admin (#2306).
 *
 * Every ops read goes through `useOps`, which keys its `useFetch` by
 * `opsCacheKey(path, query)`. A successful response is stored here under that key;
 * the next time a panel mounts the same key it starts from the stored payload instead
 * of a skeleton, and the request still runs behind it (stale-while-revalidate). A
 * failed revalidation leaves the stored payload on screen, because Nuxt resets a
 * failed fetch's `data` to its `default`, and `useOps`' `default` reads this cache.
 *
 * It lives in memory only — never `localStorage` — so a reload starts empty, and it is
 * emptied whenever the session ends (`plugins/ops-cache.client.ts`): a payload fetched
 * under one session is never drawn under another.
 */

export type OpsQuery = Record<string, string | number | boolean>

export interface OpsCacheEntry {
  value: unknown
  /** Epoch ms of the response that produced `value`. */
  fetchedAt: number
}

/** A stored payload older than this is not drawn at all: the page shows its skeleton. */
export const OPS_CACHE_TTL_MS = 10 * 60_000

/**
 * A stored payload younger than this is drawn *without* a request: it was fetched a
 * moment ago (a hover prefetch, a sibling panel), so a second request would only
 * repeat it.
 */
export const OPS_CACHE_FRESH_MS = 5_000

/**
 * A payload drawn from the cache is labelled with its age once it is older than this
 * while its revalidation has not landed. One minute is twice the portal's fastest
 * refresh cadence (the 30 s live panels, #1411): below it the label would flicker on
 * every navigation for figures no older than a live panel's.
 */
export const OPS_CACHE_LABEL_AFTER_MS = 60_000

/** Upper bound on stored payloads, so filter permutations cannot grow it forever. */
export const OPS_CACHE_MAX_ENTRIES = 200

/**
 * Strip `undefined`, `null`, and empty-string values from a query object so an unset
 * filter is omitted from the request entirely (the backend treats an absent param as
 * "no filter"). Numbers — including `0` — are preserved.
 */
export function cleanQuery(
  filters: Record<string, string | number | boolean | undefined | null>,
): OpsQuery {
  const out: OpsQuery = {}
  for (const [key, value] of Object.entries(filters)) {
    if (value === undefined || value === null || value === '') {
      continue
    }
    out[key] = value
  }
  return out
}

/**
 * The `useFetch` key — and cache key — of one ops read. Distinct per (path, query) so
 * concurrent panels hitting the same path with different filters never share an entry:
 * the Overview's unfiltered `/stats/champions` and the Champions panel's
 * region-filtered one would otherwise clobber each other's data.
 */
export function opsCacheKey(path: string, query?: OpsQuery): string {
  return query ? `ops:${path}:${JSON.stringify(query)}` : `ops:${path}`
}

export interface OpsCache {
  /** The stored entry, or `undefined` when there is none or it outlived the TTL. */
  get: (key: string) => OpsCacheEntry | undefined
  /** The stored entry only while it is younger than `OPS_CACHE_FRESH_MS`. */
  fresh: (key: string) => OpsCacheEntry | undefined
  set: (key: string, value: unknown) => void
  clear: () => void
  readonly size: number
}

export function createOpsCache(
  now: () => number = Date.now,
  maxEntries: number = OPS_CACHE_MAX_ENTRIES,
): OpsCache {
  // A `Map` iterates in insertion order, so re-inserting on write keeps the oldest
  // write first: evicting `keys().next()` drops the least recently fetched entry.
  const entries = new Map<string, OpsCacheEntry>()

  function get(key: string): OpsCacheEntry | undefined {
    const entry = entries.get(key)
    if (!entry) {
      return undefined
    }
    if (now() - entry.fetchedAt > OPS_CACHE_TTL_MS) {
      entries.delete(key)
      return undefined
    }
    return entry
  }

  return {
    get,
    fresh(key) {
      const entry = get(key)
      return entry && now() - entry.fetchedAt < OPS_CACHE_FRESH_MS ? entry : undefined
    },
    set(key, value) {
      entries.delete(key)
      entries.set(key, { value, fetchedAt: now() })
      while (entries.size > maxEntries) {
        const oldest = entries.keys().next().value
        if (oldest === undefined) {
          break
        }
        entries.delete(oldest)
      }
    },
    clear() {
      entries.clear()
    },
    get size() {
      return entries.size
    },
  }
}

/** The portal's one cache instance. */
export const opsCache = createOpsCache()

/**
 * Keys currently drawn from the cache whose revalidation has not landed yet, each with
 * the age of the payload on screen — what `OpsCacheAgeNotice` reads to say "figures from
 * N min ago". Reactive, so the notice appears and clears on its own.
 */
export const opsCacheShown = shallowReactive(new Map<string, number>())

/** Empty the cache and the on-screen registry — on logout and on session expiry. */
export function clearOpsCache(): void {
  opsCache.clear()
  opsCacheShown.clear()
}

/** The oldest `fetchedAt` among the payloads drawn from the cache, or `null`. */
export function oldestShownAt(shown: ReadonlyMap<string, number> = opsCacheShown): number | null {
  let oldest: number | null = null
  for (const fetchedAt of shown.values()) {
    if (oldest === null || fetchedAt < oldest) {
      oldest = fetchedAt
    }
  }
  return oldest
}

/** One ops read a sidebar entry's page fires on mount, to warm on hover. */
export interface OpsPrefetchTarget {
  path: string
  query?: OpsQuery
}

/**
 * What hovering or focusing a sidebar entry warms (#2306): the page's main reads, with
 * the exact query its panels send, so the warmed key is the one they mount. Kept to the
 * cheap, unfiltered reads of the pages read most — a heavy scan (`/patch-coverage`) or a
 * paginated table is not worth a request the operator may never follow.
 */
export const OPS_PREFETCH_TARGETS: Record<string, OpsPrefetchTarget[]> = {
  '/': [{ path: '/stats/overview' }, { path: '/pipeline-health' }],
  '/health': [{ path: '/pipeline-health' }, { path: '/riot-usage', query: { window: '24h' } }],
  '/aggregation': [{ path: '/stats/aggregations' }],
  '/database': [{ path: '/db/tables' }],
  '/configuration': [{ path: '/configuration' }],
}

/**
 * The route of the sidebar link under a hover/focus event's target, or `null` when the
 * target is not inside a link — the label rows, the gaps between entries.
 */
export function hoveredRoute(target: EventTarget | null): string | null {
  if (!(target instanceof Element)) {
    return null
  }
  const href = target.closest('a[href]')?.getAttribute('href')
  return href ? href.split(/[?#]/)[0] || '/' : null
}

/** A sidebar entry warmed less than this long ago is not warmed again on the next hover. */
export const OPS_PREFETCH_SKIP_MS = 30_000

/**
 * Warm the cache for a route: fetch each of its targets that was not fetched in the last
 * `OPS_PREFETCH_SKIP_MS` and has no prefetch already in flight. Failures are dropped — a
 * prefetch is a guess, and the page's own fetch reports its errors when the operator
 * actually lands there.
 */
export function createOpsPrefetcher(
  fetcher: (url: string, query?: OpsQuery) => Promise<unknown>,
  cache: OpsCache = opsCache,
  now: () => number = Date.now,
) {
  const inFlight = new Set<string>()

  return async function prefetch(route: string): Promise<void> {
    const targets = OPS_PREFETCH_TARGETS[route] ?? []
    await Promise.all(targets.map(async ({ path, query }) => {
      const key = opsCacheKey(path, query)
      const entry = cache.get(key)
      if ((entry && now() - entry.fetchedAt < OPS_PREFETCH_SKIP_MS) || inFlight.has(key)) {
        return
      }
      inFlight.add(key)
      try {
        cache.set(key, await fetcher(`/api/ops${path}`, query))
      }
      catch {
        // See above: the page's own fetch owns the error.
      }
      finally {
        inFlight.delete(key)
      }
    }))
  }
}