import type { ChampionStaticListItem } from '~~/shared/types/static-data'
import type { LeaderboardResponse } from '~~/shared/types/leaderboard'
import { defineEventHandler } from 'h3'

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

interface SitemapUrl {
  loc: string
}

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
    const response = await $fetch<LeaderboardResponse>('/api/truemains', {
      query: { page, pageSize: TRUEMAIN_PAGE_SIZE },
    })
    const rows = response.rows ?? []
    for (const row of rows) {
      const { gameName, tagLine } = row.identity
      const slug = tagLine ? `${gameName}-${tagLine}` : gameName
      urls.push({ loc: `/truemains/${encodeURIComponent(slug)}` })
    }
    // Last page reached: the service returned fewer rows than requested, or we
    // have collected every row the envelope reports.
    if (rows.length < TRUEMAIN_PAGE_SIZE || urls.length >= (response.total ?? 0)) {
      break
    }
  }
  return urls
}

export default defineEventHandler(async (): Promise<SitemapUrl[]> => {
  const [champions, truemains] = await Promise.all([
    championUrls().catch(() => [] as SitemapUrl[]),
    truemainUrls().catch(() => [] as SitemapUrl[]),
  ])
  return [...champions, ...truemains]
})
