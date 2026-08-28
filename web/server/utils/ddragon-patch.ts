import { normalizeDataDragonPatch, PATCH_PATTERN } from '~~/shared/utils/ddragon'

/**
 * Patch handling for the Data Dragon-backed static endpoints.
 *
 * Synchronised copy of `admin/server/utils/ddragon-patch.ts`: both apps hit the
 * same CDN from their own Nitro server and need the same cached resolver and the
 * same `?patch=` guard. Change one, change the twin (#1226) — the admin had
 * inlined its own *uncached* resolver, re-introducing the regression #947 fixed
 * here, while this side was missing the guard the admin had added.
 */

/**
 * DDragon's version list, newest first. Also served verbatim (and cached) by
 * `server/api/static/versions.get.ts`, which is where the front end reads it
 * from — no browser talks to the CDN directly (#1231). Exported (unlike the
 * admin twin) because this app has that second consumer; the admin does not.
 */
export const DDRAGON_VERSIONS_URL = 'https://ddragon.leagueoflegends.com/api/versions.json'

/**
 * Resolve the latest DDragon version (`16.5.1` form).
 *
 * Every static endpoint falls back to this when the caller supplies no
 * `?patch=`, which is the common case on SSR. The payloads those endpoints
 * serve are cached for an hour; before #947 this lookup in front of them was
 * not, so an uncached external round trip ran on *every* SSR of any page
 * rendering summoner spells, items, champions or a rune tree — measured at
 * 2–7 s warm on `/api/static/summoner-spells`.
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
 * `loadStaticData` degrades to `EMPTY_STATIC_DATA` instead — it catches this,
 * the same way it catches its other DDragon fetches.
 */
export const resolveLatestDDragonPatch = defineCachedFunction(
  async (): Promise<string> => {
    const versions = await $fetch<string[]>(DDRAGON_VERSIONS_URL)
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
 * rather than inline in each handler so the four static endpoints cannot drift
 * into four different notions of an acceptable patch.
 */
export function normalizeRequestedPatch(patch: string | undefined): string | null {
  const normalized = normalizeDataDragonPatch(patch)
  if (normalized !== null && !PATCH_PATTERN.test(normalized)) {
    throw createError({ statusCode: 400, statusMessage: 'Invalid patch format' })
  }
  return normalized
}
