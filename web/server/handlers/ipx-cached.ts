import { Buffer } from 'node:buffer'
import { useBase } from 'h3'
import { createIPX, createIPXH3Handler, ipxFSStorage, ipxHttpStorage } from 'ipx'

/**
 * Drop-in replacement for the `/_ipx/**` handler @nuxt/image ships, adding the
 * one thing it lacks: a server-side cache of the processed bytes.
 *
 * The module deliberately steps aside when a handler is already registered on
 * its base URL (`hasUserProvidedIPX` in @nuxt/image/dist/module.js), so this
 * file owns the route while everything else — `<NuxtImg>`, the ipx provider,
 * URL generation, modifiers — stays exactly as before.
 *
 * Why it matters: IPX caches nothing. `ipxHttpStorage` holds no store at all,
 * so every hit re-fetched the source from DDragon/CommunityDragon *twice* —
 * a HEAD from `getSourceMeta()` then a GET from `process()` — before sharp ran.
 * Measured per icon: ~76 ms of upstream round-trips against ~2 ms of actual
 * image processing. A champion page pulls ~150 icons, so every visitor whose
 * browser cache was cold made the server replay the whole set against Riot's
 * CDN. Caching the result collapses that to one fetch per asset per process.
 *
 * Cached entries never need invalidating: every source URL is pinned to a
 * patch (see `communityDragonPrefix` and the DDragon URL builders), so a given
 * URL's bytes are immutable — a new patch means new URLs.
 */

// Icons are a few KB each, but profile icons are per-player and unbounded, so
// the cache is capped rather than left to grow. ~1500 entries is the whole
// static catalogue (items, champions, perks, spells) plus room for the profile
// icons in circulation, for a few MB of heap — this box has been OOM-killed
// before, so the ceiling is deliberate. Eviction is LRU: Map preserves
// insertion order, and a hit re-inserts.
const MAX_ENTRIES = 1500

const WEEK_SECONDS = 60 * 60 * 24 * 7

interface CachedImage {
  body: Buffer
  contentType?: string
  etag?: string
  lastModified?: string
}

const cache = new Map<string, CachedImage>()

const ipx = createIPX({
  // Local `public/` sources (the bundled position icons). Paths resolve from
  // the process CWD, which is the app root in dev and `/app` in the container
  // (see web/Dockerfile: `node .output/server/index.mjs`).
  storage: ipxFSStorage({ dir: import.meta.dev ? 'public' : '.output/public' }),
  httpStorage: ipxHttpStorage({
    domains: ['ddragon.leagueoflegends.com', 'raw.communitydragon.org'],
    // Ignore upstream Cache-Control (CommunityDragon's `latest` redirects use
    // a short TTL) and advertise our own long max-age instead.
    maxAge: WEEK_SECONDS,
    ignoreCacheControl: true,
  }),
})

// `useBase` strips the route prefix before IPX parses the rest as
// `<modifiers>/<source>` — the same wrapping @nuxt/image applies.
const ipxHandler = useBase('/_ipx', createIPXH3Handler(ipx))

function restoreHeaders(event: Parameters<typeof setResponseHeader>[0], entry: CachedImage) {
  if (entry.contentType) setResponseHeader(event, 'content-type', entry.contentType)
  if (entry.etag) setResponseHeader(event, 'etag', entry.etag)
  if (entry.lastModified) setResponseHeader(event, 'last-modified', entry.lastModified)
  setResponseHeader(event, 'content-security-policy', "default-src 'none'")
}

export default defineEventHandler(async (event) => {
  // The full path carries both the modifiers and the source URL, which is
  // exactly what the output bytes depend on.
  const key = event.path

  const cached = cache.get(key)
  if (cached) {
    // Re-insert so the most recently served entries are the last to be evicted.
    cache.delete(key)
    cache.set(key, cached)
    restoreHeaders(event, cached)
    if (cached.etag && getRequestHeader(event, 'if-none-match') === cached.etag) {
      setResponseStatus(event, 304)
      return null
    }
    return cached.body
  }

  const body = await ipxHandler(event)

  // Only 200s with a real image body are worth keeping; IPX answers errors
  // with a JSON object and conditional requests with an empty 304.
  if (Buffer.isBuffer(body) && getResponseStatus(event) === 200) {
    if (cache.size >= MAX_ENTRIES) {
      const oldest = cache.keys().next()
      if (!oldest.done) cache.delete(oldest.value)
    }
    cache.set(key, {
      body,
      contentType: getResponseHeader(event, 'content-type') as string | undefined,
      etag: getResponseHeader(event, 'etag') as string | undefined,
      lastModified: getResponseHeader(event, 'last-modified') as string | undefined,
    })
  }
  else if (getResponseStatus(event) >= 400) {
    // The route rule stamps `immutable` on everything under /_ipx, which would
    // otherwise freeze a transient upstream failure in the visitor's cache for
    // a week — the icon would stay broken until they cleared it by hand.
    setResponseHeader(event, 'cache-control', 'no-store')
  }

  return body
})
