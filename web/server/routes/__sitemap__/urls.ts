import type { SitemapUrl } from '#sitemap/types'
import type { ChampionSlugMap } from '~~/shared/types/static-data'
import type { LeaderboardResponse } from '~~/shared/types/leaderboard'
import { defineEventHandler, setResponseHeader } from 'h3'

/**
 * Dynamic sitemap entries for @nuxtjs/sitemap. Static pages are discovered
 * from the file-based routes automatically; this endpoint enumerates the two
 * data-driven route families:
 *   - /champions/{slug}         — one per champion on the latest patch (#1124)
 *   - /truemains/{gameName-tagLine} — one per leaderboard player
 *
 * Both lists come from the app's own server routes (the DDragon-backed
 * champion list and the proxied backend leaderboard), so the sitemap stays in
 * sync with what the pages actually render. Each family is fetched defensively:
 * if one upstream is unavailable the other still contributes its URLs rather
 * than failing the whole sitemap.
 */

// `pageSize` is validated server-side — `TruemainsLeaderboardQueryService` caps
// it at 50 and the controller enforces that with a `[Range]` attribute — so a
// larger value is a 400 on the very first page, which the walk below swallows
// and turns into a sitemap with no profile URLs at all (#1334). This constant
// must track the API's cap, not our own idea of a reasonable page.
const TRUEMAIN_PAGE_SIZE = 50
// Cap the walk so a pathological `total` can never spin the sitemap into
// hundreds of upstream calls. 100 pages × 50 rows advertises the top 5k players
// by score, deliberately far short of the ~45k rows the leaderboard holds: a
// single sitemap tops out at 50k URLs, and the tail of the ladder is exactly
// the thin, fast-churning end that spends crawl budget without earning it.
const MAX_TRUEMAIN_PAGES = 100

async function championUrls(): Promise<SitemapUrl[]> {
  // The slug map, not the champion list: the sitemap must advertise the exact
  // URLs the pages canonicalise to, and a numeric `loc` would put every
  // champion in the sitemap as a 301 to somewhere else (#1124). Same source the
  // router and the link builders read, so the three cannot disagree.
  const slugs = await $fetch<ChampionSlugMap>('/api/static/champion-slugs')
  return Object.values(slugs).map(slug => ({ loc: `/champions/${slug}` }))
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
        // `{gameName}-{tagLine}` is the app-wide profile slug (see
        // TruemainsPanel / LeaderboardRow); the `[nameTag]` route passes it
        // opaque to the backend, which resolves it. The `-` separator is
        // unambiguous because Riot tagLines never contain a hyphen.
        //
        // Deliberately **not** `encodeURIComponent`d, unlike the `to`/`href`
        // the components above build. @nuxtjs/sitemap percent-encodes every
        // `loc` on the way into the XML, so pre-encoding here is applied twice:
        // a space becomes `%2520` and `Álec Lightwood` ends up advertised as
        // `%25C3%2581lec%2520Lightwood`, which the `[nameTag]` route hands to
        // the backend as the literal text `%C3%81lec%20Lightwood` — a 404. Riot
        // IDs are full Unicode, so this hit 2,334 of the first 5,000 profiles.
        const slug = tagLine ? `${gameName}-${tagLine}` : gameName
        urls.push({ loc: `/truemains/${slug}` })
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
    // Both families degrade to an empty list on any upstream failure and the
    // sitemap still renders successfully, so a whole route family can vanish
    // from it with nothing anywhere saying so — which is how #1334 shipped
    // unnoticed. The graceful degradation is deliberate; the silence was not.
    if (champions.length === 0) {
      console.warn('[sitemap] champion source contributed no URLs')
    }
    if (truemains.length === 0) {
      console.warn('[sitemap] leaderboard walk contributed no profile URLs')
    }
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
