import type { SitemapUrl } from '#sitemap/types'
import type { ChampionSummaryResponse } from '~~/shared/types/champions'
import type { ChampionSlugMap } from '~~/shared/types/static-data'
import { defineEventHandler, setResponseHeader } from 'h3'
import { championLastmodById } from '~~/shared/utils/sitemap-lastmod'

/**
 * Dynamic sitemap entries for @nuxtjs/sitemap. Static pages are discovered from
 * the file-based routes automatically; this endpoint enumerates the one
 * data-driven family the sitemap advertises:
 *   - /champions/{slug} — one per champion on the latest patch (#1124),
 *                         carrying a day-precision `lastmod` (#1256)
 *
 * The list comes from the app's own server route, so the sitemap stays in sync
 * with what the pages actually render, and is fetched defensively: an upstream
 * outage costs the dynamic entries, not the whole sitemap.
 *
 * **Player profiles are deliberately absent.** `/truemains/{nameTag}` fetches
 * its profile client-only (`useTruemainFetch`, #862), so the server-rendered
 * document is a skeleton: the same generic description on every page, the
 * player's name appearing once in the title, and nothing else. Advertising
 * 5,000 of those hands a crawler 5,000 near-duplicate empty documents and
 * buries the champion pages, which are the only fully server-rendered content
 * here. They stay reachable — `/truemains` is in the sitemap and links them, so
 * a crawler can still descend — they are simply not advertised. Revisit if the
 * profile page ever renders its content server-side (#1337).
 */

async function championUrls(): Promise<SitemapUrl[]> {
  // The slug map, not the champion list: the sitemap must advertise the exact
  // URLs the pages canonicalise to, and a numeric `loc` would put every
  // champion in the sitemap as a 301 to somewhere else (#1124). Same source the
  // router and the link builders read, so the three cannot disagree.
  //
  // Freshness (#1256), day-precision — see `toSitemapDay` for why not finer.
  // The slug map is what decides *which* URLs exist; the directory only
  // decorates them, so it fails on its own: a champion-directory outage costs
  // the `lastmod`, never the URLs. A champion the directory doesn't mention
  // (the days after a patch flip, before its lane is folded) is emitted without
  // one rather than with a fabricated date.
  //
  // The two are independent, so they go out together — the slug map's rejection
  // still reaches the caller's catch, which is what drops the family.
  const [slugs, summaries] = await Promise.all([
    $fetch<ChampionSlugMap>('/api/static/champion-slugs'),
    $fetch<ChampionSummaryResponse[]>('/api/champions').catch(() => null),
  ])
  const lastmodById = championLastmodById(summaries)

  return Object.entries(slugs).map(([championId, slug]) => {
    const lastmod = lastmodById.get(Number(championId))
    return lastmod
      ? { loc: `/champions/${slug}`, lastmod }
      : { loc: `/champions/${slug}` }
  })
}

// Cache the fan-out at the origin (not just via downstream CDNs): @nuxtjs/sitemap
// caches the rendered sitemap, but this route is publicly reachable, so without
// this every direct hit would pass straight through to the upstreams. Mirrors
// server/api/static/champions.get.ts. The cache wraps the function (not the
// handler) so the handler keeps full control of the response Cache-Control
// header below.
const loadSitemapUrls = defineCachedFunction(
  async (): Promise<SitemapUrl[]> => {
    const champions = await championUrls().catch(() => [] as SitemapUrl[])
    // Degrading to the static pages is deliberate; doing it silently is not.
    // #1334 shipped precisely because an empty family still rendered a valid
    // sitemap and nothing anywhere said a route family had gone missing.
    if (champions.length === 0) {
      console.warn('[sitemap] champion source contributed no URLs')
    }
    return champions
  },
  { maxAge: 60 * 60, name: 'sitemap-urls', getKey: () => 'all' },
)

export default defineEventHandler(async (event): Promise<SitemapUrl[]> => {
  // Let shared caches absorb repeats too (defense in depth alongside the
  // origin function cache above).
  setResponseHeader(event, 'Cache-Control', 'public, max-age=3600, s-maxage=3600')
  return loadSitemapUrls()
})
