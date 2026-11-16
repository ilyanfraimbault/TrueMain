// Shared between nuxt.config.ts (the browser-facing `cache-control` route
// rule) and server/handlers/ipx-cached.ts (IPX's own upstream `maxAge`, which
// only affects what @nuxt/image would have written): the same duration
// should govern both since the whole point is that a URL's bytes never
// change once patched, browser and server agree on how long to trust that.
export const IPX_CACHE_SECONDS = 60 * 60 * 24 * 7

/**
 * The hosts IPX fetches from: Riot's static data, and the image hosts of the
 * channels a known player's picture is read from (#2446). The Api only ever
 * stores a picture from these (`PersonaPictureLookup`), and the desktop app's CSP
 * lists them too (`desktop/src-tauri/tauri.conf.json`): the three move together.
 */
export const IPX_SOURCE_DOMAINS = [
  'ddragon.leagueoflegends.com',
  'raw.communitydragon.org',
  'yt3.ggpht.com',
  'yt3.googleusercontent.com',
  'static-cdn.jtvnw.net',
  'files.kick.com',
  'profile.img.sooplive.co.kr',
  'nng-phinf.pstatic.net',
]

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

export interface IpxShape {
  width: number
  height?: number
  format: 'webp'
}

/**
 * The shapes the site fetches images in (#1000): one fixed fetch per kind,
 * whatever size it is displayed at, so one asset is one cache entry. The
 * components build their URLs from these through `useImage()`
 * (`app/composables/useCanonicalIcon.ts`); the warm-up and the disk cache's
 * filter (`server/utils/warmup-catalog.ts`) through {@link ipxModifiers}.
 */
export const IPX_SHAPES = {
  /** Every icon: Data Dragon ships items at 64 × 64, and larger sources downscale cleanly. */
  icon: { width: 64, height: 64, format: 'webp' },
  /** A banner's splash art: the hero is at most ~1280 px wide. */
  splash: { width: 1280, format: 'webp' },
  /** A champion's loading-screen art, at Data Dragon's own 308 px, never enlarged. */
  loadingArt: { width: 308, format: 'webp' },
} as const satisfies Record<string, IpxShape>

/**
 * The modifiers segment of a shape's `/_ipx/…` URL, as @nuxt/image's IPX
 * provider writes it: the modifiers in order, a width and a height folded into
 * a trailing `s_WxH`. `tests/nuxt/canonical-image-shapes.test.ts` holds the two
 * writers to the same output.
 */
export function ipxModifiers(shape: IpxShape): string {
  const { width, height, ...rest } = shape
  const sized = height === undefined ? { width, ...rest } : { ...rest, resize: `${width}x${height}` }
  const keys: Record<string, string> = { width: 'w', format: 'f', resize: 's' }
  return Object.entries(sized).map(([key, value]) => `${keys[key]}_${value}`).join('&')
}
