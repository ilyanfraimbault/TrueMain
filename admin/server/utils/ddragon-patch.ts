import { normalizeDataDragonPatch, PATCH_PATTERN } from '~~/shared/utils/ddragon'

/**
 * Patch handling for the Data Dragon-backed static endpoints.
 *
 * Synchronised copy of `web/server/utils/ddragon-patch.ts`: both apps hit the
 * same CDN from their own Nitro server and need the same cached resolver and the
 * same `?patch=` guard. Change one, change the twin (#1226) — this side had
 * inlined its own *uncached* resolver, re-introducing the regression #947 fixed
 * on the web side, while web was missing the guard added here.
 */

const VERSIONS_URL = 'https://ddragon.leagueoflegends.com/api/versions.json'

/**
 * Resolve the latest DDragon version (`16.5.1` form).
 *
 * `/api/static/champions` falls back to this when the caller supplies no
 * `?patch=`, and `useChampionStatic()` never sends one — so this is the nominal
 * path behind /champions, /candidates, /accounts, /patch-coverage and
 * /data-quality, not an edge case. The champion payload it feeds is cached for
 * an hour; before #1226 this lookup in front of it was not, so an uncached
 * external round trip ran on *every* call — and the admin renders client-side
 * (`ssr: false`), so that is once per page load. #947 measured the same round
 * trip at 2–7 s warm on the web side.
 *
 * Riot ships a patch every ~2 weeks, so the TTL here can be much longer than
 * the 1 h payload TTL. `defineCachedFunction` is stale-while-revalidate by
 * default: once the entry ages out, the request that trips it still gets the
 * previous patch immediately and the refresh happens in the background, so a
 * new patch never puts the round trip back on the critical path. The cost is
 * that a freshly released patch surfaces up to one TTL late, which is why this
 * is hours and not days.
 *
 * Failure contract: throws a 502. The static endpoints cannot answer without a
 * patch, and failing once beats caching a bad answer for hours.
 */
export const resolveLatestDDragonPatch = defineCachedFunction(
  async (): Promise<string> => {
    const versions = await $fetch<string[]>(VERSIONS_URL)
    const latest = versions[0]
    if (!latest) {
      throw createError({ statusCode: 502, statusMessage: 'DDragon returned no versions' })
    }
    return latest
  },
  {
    maxAge: 6 * 60 * 60,
    name: 'ddragon-latest-patch',
    getKey: () => 'latest',
  },
)

/**
 * Normalize a caller-supplied `?patch=`, or `null` when none was supplied (the
 * caller then falls back to `resolveLatestDDragonPatch`).
 *
 * Anything that is not a `major.minor.patch` patch is rejected with a 400
 * *before* it reaches a CDN URL or a cache key — see `PATCH_PATTERN`. Kept here
 * rather than inline in the handler so the guard stays in step with the web
 * twin, which applies it to four static endpoints.
 */
export function normalizeRequestedPatch(patch: string | undefined): string | null {
  const normalized = normalizeDataDragonPatch(patch)
  if (normalized !== null && !PATCH_PATTERN.test(normalized)) {
    throw createError({ statusCode: 400, statusMessage: 'Invalid patch format' })
  }
  return normalized
}
