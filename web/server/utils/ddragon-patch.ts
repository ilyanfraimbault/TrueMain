const VERSIONS_URL = 'https://ddragon.leagueoflegends.com/api/versions.json'

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
 * Failure contract: throws a 502. The three static endpoints cannot answer
 * without a patch, and failing once beats caching a bad answer for hours.
 * `loadStaticData` degrades to `EMPTY_STATIC_DATA` instead — it catches this,
 * the same way it catches its other DDragon fetches.
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
