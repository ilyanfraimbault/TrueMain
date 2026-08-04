import { Buffer } from 'node:buffer'
import { useBase } from 'h3'
import { createIPX, createIPXH3Handler, ipxFSStorage, ipxHttpStorage } from 'ipx'
import { IPX_CACHE_SECONDS } from '~~/shared/utils/ipx'
import { createBoundedByteCache } from '../utils/bounded-byte-cache'
import { createPatchRetention, isOutsideRetention } from '../utils/ipx-patch-retention'

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

// `/_ipx/**` is a public, unauthenticated route: anyone can request any
// modifiers against any path under the allow-listed domains, and
// CommunityDragon serves more than icons on that domain — splash art and
// centered/uncentered assets run several MB each. IPX clamps resize
// dimensions to the source's own size (no upscaling), so a request can't
// force a small icon to balloon, but it can't stop someone from repeatedly
// requesting distinct multi-MB assets. So the cache (see
// server/utils/bounded-byte-cache.ts) is bounded by total bytes, not entry
// count — capping by count alone would assume every cacheable response is
// icon-sized, which the route itself doesn't guarantee. This box has been
// OOM-killed before (see the postgres /dev/shm and pattern-agg incidents), so
// the ceiling is deliberate.
const cache = createBoundedByteCache<CachedImage>({
  maxBytes: 64 * 1024 * 1024,
  // A single response above this is unusual (icons are a few KB; even a large
  // champion splash art profile shot is well under this) and not worth
  // holding onto — it's still served, just not retained, so one big request
  // can't crowd out the icons that make up the rest of the catalogue.
  maxEntryBytes: 2 * 1024 * 1024,
})

// Patch turnover is the one eviction the LRU gets wrong: on release day every
// key changes at once, so the outgoing patch's bytes sit in the budget,
// unreachable, exactly when the cache is cold. See ipx-patch-retention.ts.
const patchRetention = createPatchRetention()

interface CachedImage {
  body: Buffer
  byteLength: number
  contentType?: string
  etag?: string
  lastModified?: string
}

const ipx = createIPX({
  // Local `public/` sources (the bundled position icons). Paths resolve from
  // the process CWD, which is the app root in dev and `/app` in the container
  // (see web/Dockerfile: `node .output/server/index.mjs`).
  storage: ipxFSStorage({ dir: import.meta.dev ? 'public' : '.output/public' }),
  httpStorage: ipxHttpStorage({
    domains: ['ddragon.leagueoflegends.com', 'raw.communitydragon.org'],
    // Ignore upstream Cache-Control (CommunityDragon's `latest` redirects use
    // a short TTL) and advertise our own long max-age instead.
    maxAge: IPX_CACHE_SECONDS,
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
    // Sweep before storing so the incoming patch's first entry doesn't share
    // the budget with the patch that just aged out. Returns non-null only on
    // the first request of a newer patch, so this is a no-op on every other
    // cache miss.
    const retained = patchRetention.observe(key)
    if (retained) {
      const dropped = cache.purge(cacheKey => isOutsideRetention(cacheKey, retained))
      if (dropped > 0) {
        console.info(`[ipx] new patch observed, dropped ${dropped} cached image(s) from expired patches`)
      }
    }

    cache.set(key, {
      body,
      byteLength: body.byteLength,
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
