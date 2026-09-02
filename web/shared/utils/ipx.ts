// Shared between nuxt.config.ts (the browser-facing `cache-control` route
// rule) and server/handlers/ipx-cached.ts (IPX's own upstream `maxAge`, which
// only affects what @nuxt/image would have written): the same duration
// should govern both since the whole point is that a URL's bytes never
// change once patched, browser and server agree on how long to trust that.
export const IPX_CACHE_SECONDS = 60 * 60 * 24 * 7 // 7 days

/**
 * The route `/_ipx/**` is served from, shared so the three places that need it
 * cannot drift: the Nitro route registration, the browser cache-control rule, and
 * the handler that has to strip this prefix before handing the rest to IPX.
 *
 * That last one is the reason this is a constant rather than three literals. IPX
 * parses whatever path it receives as `<modifiers>/<source>`, so a prefix left in
 * place is silently read as the modifiers segment.
 */
export const IPX_ROUTE_BASE = '/_ipx'

/**
 * The path IPX must parse, given the path this handler was reached on.
 *
 * IPX reads whatever it receives as `<modifiers>/<source>`, and nothing strips the
 * route prefix for it. Leaving `/_ipx` in place makes IPX read `_ipx` as the
 * modifiers segment; the remainder then no longer looks like a URL, so it is routed
 * to the filesystem storage, which answers `403 IPX_FORBIDDEN_PATH` — for every
 * image on the site at once.
 */
export function ipxRequestPath(eventPath: string): string {
  return eventPath.startsWith(IPX_ROUTE_BASE)
    ? eventPath.slice(IPX_ROUTE_BASE.length)
    : eventPath
}
