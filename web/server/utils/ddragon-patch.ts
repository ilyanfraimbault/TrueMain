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
 * DDragon's version list, newest first. Also served verbatim by
 * `server/api/static/versions.get.ts`, which is where the front end reads it
 * from — no browser talks to the CDN directly (#1231). Exported (unlike the
 * admin twin) because this app has that second consumer; the admin does not.
 */
export const DDRAGON_VERSIONS_URL = 'https://ddragon.leagueoflegends.com/api/versions.json'

/**
 * DDragon's published versions, newest first (`["16.5.1", "16.4.1", …]`).
 *
 * The single upstream read behind every patch decision in this app: the latest
 * patch, the availability check in {@link resolveDDragonVersion}, and the list
 * the patch selector is built from all derive from this one cached entry
 * instead of fetching `versions.json` again (they used to be two independent
 * fetches).
 *
 * Before #947 this lookup was uncached, so an external round trip ran on
 * *every* SSR of any page rendering summoner spells, items, champions or a rune
 * tree — measured at 2–7 s warm on `/api/static/summoner-spells`.
 *
 * Riot ships a patch every ~2 weeks, so the TTL here can be much longer than
 * the 1 h payload TTL. `defineCachedFunction` is stale-while-revalidate by
 * default: once the entry ages out, the request that trips it still gets the
 * previous list immediately and the refresh happens in the background, so a
 * new patch never puts the round trip back on the critical path. The cost is
 * that a freshly released patch surfaces up to one TTL late, which is why this
 * is hours and not days.
 *
 * Failure contract: throws a 502 rather than answering an empty list, so a
 * transient outage isn't cached for hours as "there are no patches".
 */
export const loadDDragonVersions = defineCachedFunction(
  async (): Promise<string[]> => {
    const versions = await $fetch<string[]>(DDRAGON_VERSIONS_URL)
    if (!versions.length) {
      throw createError({ statusCode: 502, statusMessage: 'DDragon returned no versions' })
    }
    return versions
  },
  {
    maxAge: 6 * 60 * 60,
    name: 'ddragon-versions',
    getKey: () => 'versions',
  },
)

/**
 * Map a requested game patch onto a version DDragon has actually published.
 *
 * **Riot ships a patch hours to days before DDragon publishes it**, and the
 * front end asks for the patch the *API* reports — the live game patch. Pinning
 * the CDN path to a version that does not exist yet does not 404, it answers
 * the bucket's **403 AccessDenied**, which surfaced as
 * "Failed to load the champion list" on the whole champions page for the first
 * part of every patch cycle (#1693): the page blanks when any one of its four
 * static sources errors, and `/api/static/{items,champions,summoner-spells}`
 * all failed at once.
 *
 * So: the newest published build of the requested `major.minor` when DDragon
 * has one — which also covers DDragon numbering a build something other than
 * the `.1` `normalizeDataDragonPatch` assumes — and its newest version overall
 * otherwise. Item names, champion names and icons barely move between two
 * patches; a page rendered from the previous patch's assets is a far better
 * answer than no page. This mirrors what the ingestor already does for champion
 * statics (`Data/Statics/DataDragonChampionStaticsProvider`) and what
 * `api/static/rune-tree.get.ts` does for CommunityDragon's mirror-image lag.
 *
 * Takes an already-normalized patch (see {@link normalizeRequestedPatch}), so
 * validation stays at the edge where a bad `?patch=` can still be rejected with
 * a 400 rather than degraded into a fallback. `null` — no `?patch=` at all,
 * which is the common case on SSR — resolves to the latest version.
 */
export async function resolveDDragonVersion(normalizedPatch: string | null): Promise<string> {
  const versions = await loadDDragonVersions()
  const latest = versions[0]
  if (!latest) {
    throw createError({ statusCode: 502, statusMessage: 'DDragon returned no versions' })
  }
  if (!normalizedPatch) return latest

  const [major, minor] = normalizedPatch.split('.')
  return versions.find(version => version.startsWith(`${major}.${minor}.`)) ?? latest
}

/**
 * Normalize a caller-supplied `?patch=`, or `null` when none was supplied (the
 * caller then falls back to the latest version).
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
