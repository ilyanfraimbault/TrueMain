import { loadDDragonVersions } from '~~/server/utils/ddragon-patch'

/**
 * DDragon's version list, newest first (`["16.5.1", "16.4.1", …]`).
 *
 * Exists so the browser never talks to `ddragon.leagueoflegends.com` itself
 * (#1231). `useDDragonVersions` was the last front-end fetch pointing straight
 * at a third-party CDN, on nearly every page — no shared cache, one visitor's
 * fetch warming nothing for the next, and a DDragon outage surfacing as a
 * visitor-visible failure instead of a server-side cache miss. Behind this
 * handler it is one upstream call per TTL per instance, shared with every
 * `/api/static/*` sibling.
 *
 * Not patch-scoped, obviously — this is the list the patch is picked *from*.
 * A thin pass-through over `loadDDragonVersions` rather than a second cached
 * fetch of its own: the resolver behind `/api/static/*` reads the same list to
 * decide which version a requested patch maps to, and the selector must offer
 * patches from exactly the list that resolution is done against. One cache
 * entry, one upstream call.
 *
 * Failure contract lives in `loadDDragonVersions`: it throws rather than
 * answering an empty array, so a transient outage isn't cached as "there are no
 * patches". Callers default to `[]` on their side.
 */
export default defineEventHandler((): Promise<string[]> => loadDDragonVersions())
