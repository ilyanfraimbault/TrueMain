import type { SitemapUrl } from '#sitemap/types'
import type { ChampionStaticListItem } from '~~/shared/types/static-data'
import type { LeaderboardResponse } from '~~/shared/types/leaderboard'
import { defineEventHandler, setResponseHeader } from 'h3'

/**
 * Dynamic sitemap entries for @nuxtjs/sitemap. Static pages are discovered
 * from the file-based routes automatically; this endpoint enumerates the two
 * data-driven route families:
 *   - /champions/{championId}   — one per champion on the latest patch
 *   - /truemains/{gameName-tagLine} — one per leaderboard player
 *
 * Both lists come from the app's own server routes (the DDragon-backed
 * champion list and the proxied backend leaderboard), so the sitemap stays in
 * sync with what the pages actually render. Each family is fetched defensively:
 * if one upstream is unavailable the other still contributes its URLs rather
 * than failing the whole sitemap.
 */

// Cap the leaderboard walk so a pathological `total` can never spin the
// sitemap into hundreds of upstream calls. 100 pages × 100 rows = 10k players,
// comfortably above the current roster.
const TRUEMAIN_PAGE_SIZE = 100
const MAX_TRUEMAIN_PAGES = 100

async function championUrls(): Promise<SitemapUrl[]> {
  const champions = await $fetch<ChampionStaticListItem[]>('/api/static/champions')
  return champions.map(champion => ({ loc: `/champions/${champion.championId}` }))
}

async function truemainUrls(): Promise<SitemapUrl[]> {
  const urls: SitemapUrl[] = []
  for (let page = 1; page <= MAX_TRUEMAIN_PAGES; page++) {
    try {
      const response = await $fetch<LeaderboardResponse>('/api/truemains', {
        query: { page, pageSize: TRUEMAIN_PAGE_SIZE },
      })
      const rows = response.rows
      for (const row of rows) {
        const { gameName, tagLine } = row.identity
        const slug = tagLine ? `${gameName}-${tagLine}` : gameName
        urls.push({ loc: `/truemains/${encodeURIComponent(slug)}` })
      }
      // Last page reached: the service returned fewer rows than requested, or
      // we have collected every row the envelope reports.
      if (rows.length < TRUEMAIN_PAGE_SIZE || urls.length >= response.total) {
        break
      }
    }
    catch {
      // Any failure — a transient network error or a malformed payload (e.g.
      // `rows` missing, which would throw on the for...of above) — stops the
      // walk and keeps the pages already collected, rather than bubbling to the
      // caller's catch and discarding them. Reading `rows` inside the try is
      // what makes the contract-violation case degrade gracefully too, so no
      // defensive `?? []` is needed.
      break
    }
  }
  return urls
}

// Cache the fan-out at the origin (not just via downstream CDNs): @nuxtjs/sitemap
// caches the rendered sitemap, but this route is publicly reachable and a single
// uncached call fans out to up to MAX_TRUEMAIN_PAGES backend requests. Wrapping
// the work in Nitro's function cache caps that fan-out to once per maxAge
// regardless of request volume, so a direct-hit flood can't amplify 1 request
// into 100 backend calls. Mirrors server/api/static/champions.get.ts. The cache
// wraps the function (not the handler) so the handler keeps full control of the
// response Cache-Control header below.
const loadSitemapUrls = defineCachedFunction(
  async (): Promise<SitemapUrl[]> => {
    const [champions, truemains] = await Promise.all([
      championUrls().catch(() => [] as SitemapUrl[]),
      truemainUrls().catch(() => [] as SitemapUrl[]),
    ])
    return [...champions, ...truemains]
  },
  { maxAge: 60 * 60, name: 'sitemap-urls', getKey: () => 'all' },
)

export default defineEventHandler(async (event): Promise<SitemapUrl[]> => {
  // Let shared caches absorb repeats too (defense in depth alongside the
  // origin function cache above).
  setResponseHeader(event, 'Cache-Control', 'public, max-age=3600, s-maxage=3600')
  return loadSitemapUrls()
})
