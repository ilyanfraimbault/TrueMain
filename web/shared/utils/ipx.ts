// Shared between nuxt.config.ts (the browser-facing `cache-control` route
// rule) and server/handlers/ipx-cached.ts (IPX's own upstream `maxAge`, which
// only affects what @nuxt/image would have written): the same duration
// should govern both since the whole point is that a URL's bytes never
// change once patched, browser and server agree on how long to trust that.
export const IPX_CACHE_SECONDS = 60 * 60 * 24 * 7 // 7 days
