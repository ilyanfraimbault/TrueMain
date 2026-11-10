/**
 * The site's pages, one entry each, and what the server needs to know about
 * them: which ones the shared HTML cache stores and under which query keys,
 * which ones a worker renders before it opens, which sitemap lists them and
 * which are served stale-while-revalidate. `server/utils/html-cache.ts`,
 * `server/utils/warmup-catalog.ts` and `nuxt.config.ts` (sitemap, route rules)
 * all read this table, so a new page is one entry here.
 *
 * `path` is the route as Vue Router spells it, `:param` for a dynamic segment.
 */
export interface SitePage {
  path: string
  /**
   * Stored by the shared HTML cache (#2425), keyed on these query keys — the
   * ones the page reads. A request carrying any other key renders without being
   * stored, so a crawler's junk parameters cannot fill the budget.
   */
  htmlCacheParams?: readonly string[]
  /** Rendered by a worker before the edge opens the site (#2425). Static paths only. */
  warm?: boolean
  /** The sitemap family that lists it (#1538), or none. */
  sitemap: 'pages' | 'champions' | 'players' | 'truemains' | null
  /** Served from the Nitro cache and re-rendered in the background after this many seconds (#1617). */
  swrSeconds?: number
}

/** The query keys every champion slice reads (`useChampionFilters`). */
const CHAMPION_SLICE_PARAMS = ['patch', 'position', 'elo', 'everyone', 'vs'] as const

/**
 * The three text pages fetch nothing of their own, so their HTML depends on the
 * shell only. `swr` rather than prerendering: a prerendered page would carry the
 * build environment's `runtimeConfig.public` (no analytics, no version stamp, the
 * production canonical on preprod). The hour matches the champion slug map's own
 * server cache, so a cached page is never staler than a rendered one.
 */
const TEXT_PAGE_SWR_SECONDS = 60 * 60

export const SITE_PAGES: readonly SitePage[] = [
  { path: '/', htmlCacheParams: [], warm: true, sitemap: 'pages' },
  { path: '/champions', htmlCacheParams: [...CHAMPION_SLICE_PARAMS, 'championId', 'page', 'sort', 'order'], warm: true, sitemap: 'pages' },
  { path: '/champions/tierlist', htmlCacheParams: [...CHAMPION_SLICE_PARAMS, 'championId'], warm: true, sitemap: 'pages' },
  { path: '/champions/:slug', htmlCacheParams: CHAMPION_SLICE_PARAMS, sitemap: 'champions' },
  { path: '/truemains', htmlCacheParams: ['region', 'position', 'championId', 'otpOnly', 'sort', 'page'], warm: true, sitemap: 'pages' },
  // A per-visitor localStorage list: nothing stable to index (the page is `noindex`).
  { path: '/truemains/favorites', sitemap: null },
  // Profiles are not advertised (#1337, `server/routes/__sitemap__/urls.ts`).
  { path: '/truemains/:nameTag', sitemap: 'truemains' },
  { path: '/truemains/:nameTag/champions/:slug', sitemap: null },
  { path: '/players/:slug', sitemap: 'players' },
  { path: '/matchup', warm: true, sitemap: 'pages' },
  // The legacy redirect to `/matchup` (#939), kept for old links.
  { path: '/builder', sitemap: null },
  { path: '/download', warm: true, sitemap: 'pages' },
  { path: '/about', warm: true, sitemap: 'pages', swrSeconds: TEXT_PAGE_SWR_SECONDS },
  { path: '/privacy', sitemap: 'pages', swrSeconds: TEXT_PAGE_SWR_SECONDS },
  { path: '/terms', sitemap: 'pages', swrSeconds: TEXT_PAGE_SWR_SECONDS },
]

/** A route as a sitemap or route-rule glob: `/champions/:slug` → `/champions/*`. */
export function sitePageGlob(path: string): string {
  return path.replace(/:[^/]+/g, '*')
}

/** A route as a matcher for a request path: one dynamic segment matches any non-empty segment. */
export function sitePageMatcher(path: string): RegExp {
  const pattern = path.split('/').map(segment => (segment.startsWith(':') ? '[^/]+' : segment.replace(/[.*+?^${}()|[\]\\]/g, String.raw`\$&`))).join('/')
  return new RegExp(`^${pattern}$`)
}
