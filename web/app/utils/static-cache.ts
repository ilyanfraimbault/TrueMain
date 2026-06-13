// Mirrors the 1h maxAge on `defineCachedFunction` in server/api/static/*.
// Reusing entries past this point would let stale DDragon / CommunityDragon
// data linger across patch releases.
export const STATIC_CACHE_TTL_MS = 60 * 60 * 1000

// Narrow shape we actually need from `NuxtApp`. Keeping the contract explicit
// here lets the helper be unit-tested without pulling in the Nuxt runtime.
export interface PayloadHost {
  payload: { data: Record<string, unknown> }
  static: { data: Record<string, unknown> }
}

// Sibling key holding the timestamp for a cached entry. We deliberately don't
// wrap the payload itself (no `{ data, fetchedAt }` shape) so consumers can
// keep treating `data.value` as the raw response.
function timestampKey(key: string): string {
  return `${key}::fetchedAt`
}

function readTimestamp(key: string, host: PayloadHost): number | undefined {
  return (host.payload.data[timestampKey(key)] ?? host.static.data[timestampKey(key)]) as number | undefined
}

/**
 * `getCachedData` for `useFetch` / `useAsyncData` that reuses a previously
 * resolved value across navigations, expiring after {@link STATIC_CACHE_TTL_MS}.
 *
 * Without this, `useFetch` re-issues its request every time the composable
 * mounts on a new page (`/champions` -> `/truemains` -> `/champions` triggers two
 * fetches). The Nitro layer already caches the upstream response for 1h, so
 * we mirror that on the client to skip the round trip entirely.
 *
 * SSR-hydrated entries lack a sibling timestamp on the first client tick;
 * treat them as fresh so the client doesn't immediately refetch what the
 * server just sent.
 *
 * The `isServer` switch is defence-in-depth for #149: every consumer pairs
 * this helper with `server: false`, so the fetch handler is already skipped
 * during SSR. But if a callsite ever forgot the option (or if Nuxt's
 * `useLazyAsyncData` ever started passing through a SSR-side payload entry),
 * returning a cached value on the server would make the SSR render data-aware
 * while the client still hydrates from the empty initial state — exactly the
 * `<ul>`/`<UProgress>` hydration mismatch reported on filtered `/champions`
 * routes. Forcing `undefined` server-side keeps the SSR output deterministic.
 */
export function getStaticCachedData<T>(
  key: string,
  host: PayloadHost,
  now: number = Date.now(),
  isServer: boolean = import.meta.server === true,
): T | undefined {
  if (isServer) return undefined

  const cached = (host.payload.data[key] ?? host.static.data[key]) as T | undefined
  if (cached === undefined || cached === null) return undefined

  const fetchedAt = readTimestamp(key, host)
  if (fetchedAt !== undefined && now - fetchedAt > STATIC_CACHE_TTL_MS) {
    return undefined
  }
  return cached
}

/**
 * Stamps a freshly-resolved value so {@link getStaticCachedData} can compute
 * its age on the next remount. Call from the fetch handler after the network
 * round trip resolves.
 */
export function markStaticFetched(key: string, host: PayloadHost, now: number = Date.now()): void {
  host.payload.data[timestampKey(key)] = now
}
