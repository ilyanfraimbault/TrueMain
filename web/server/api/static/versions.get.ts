import { DDRAGON_VERSIONS_URL } from '~~/server/utils/ddragon-patch'

/**
 * DDragon's version list, newest first (`["16.5.1", "16.4.1", …]`).
 *
 * Exists so the browser never talks to `ddragon.leagueoflegends.com` itself
 * (#1231). `useDDragonVersions` was the last front-end fetch pointing straight
 * at a third-party CDN, on nearly every page — no shared cache, one visitor's
 * fetch warming nothing for the next, and a DDragon outage surfacing as a
 * visitor-visible failure instead of a server-side cache miss. Behind this
 * handler it is one upstream call per hour per instance, exactly like its
 * `/api/static/*` siblings.
 *
 * Not patch-scoped, obviously — this is the list the patch is picked *from*.
 * Cached at the handler rather than through a `defineCachedFunction` because
 * there is no argument to key on: same TTL, one less indirection.
 *
 * Failure contract matches `resolveLatestDDragonPatch`: throw rather than
 * answer an empty array, so a transient outage isn't cached for an hour as
 * "there are no patches". Callers default to `[]` on their side.
 */
export default defineCachedEventHandler(
  async (): Promise<string[]> => {
    const versions = await $fetch<string[]>(DDRAGON_VERSIONS_URL)
    if (!versions.length) {
      throw createError({ statusCode: 502, statusMessage: 'DDragon returned no versions' })
    }
    return versions
  },
  {
    maxAge: 60 * 60,
    name: 'ddragon-versions',
    getKey: () => 'versions',
  },
)
