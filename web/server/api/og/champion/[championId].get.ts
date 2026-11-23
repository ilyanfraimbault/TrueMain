import type { ChampionSummaryReadModel } from '#shared/contract/api'
import type { ChampionOgCard } from '~~/shared/types/og-card'
import type { ChampionStaticData } from '~~/shared/types/static-data'
import { selectChampionSummaryRow } from '~~/shared/utils/og-card'
import { readChampionId, readChampionSliceQuery, type ChampionSliceQuery } from '~~/server/utils/champion-slice-query'
import { defineCachedRead, readOrAbsent, visitorAddress } from '~~/server/utils/upstream'

/**
 * Card model for `/champions/{id}`'s share image (#926).
 *
 * Why this exists at all: the champion page fetches everything `server: false`
 * (the #149 hydration fix), so at SSR time — when the `og:image` URL is minted
 * — the page holds no numbers to hand to the template. The OG URL therefore
 * carries only the identifiers, and this route resolves the real slice when a
 * crawler actually renders the image. That keeps the cost on the unfurl path
 * instead of adding an SSR round-trip to every human page view.
 *
 * The numbers come from `GET /champions` — the same directory rows the
 * `/champions` list and the tier list print — so the card can never disagree
 * with the site about a champion's win rate, pick rate or tier.
 */

/**
 * Resolves the card. An absence degrades only its own block: no static data
 * costs the portrait and the name, no directory row costs the stats. A failing
 * upstream is never cached (`server/utils/upstream.ts`, #1557): that one render
 * gets the branded card, and the next one retries.
 */
const loadChampionOgCard = defineCachedRead<ChampionOgCard, [number, ChampionSliceQuery, string?]>(
  async (championId: number, query: ChampionSliceQuery, forwardedFor?: string): Promise<ChampionOgCard> => {
    const [staticData, summaries] = await Promise.all([
      readOrAbsent<ChampionStaticData>(`/api/static/${championId}`, { forwardedFor }),
      readOrAbsent<ChampionSummaryReadModel[]>('/api/champions', {
        query: {
          patch: query.patch ?? undefined,
          eloBracket: query.eloBracket ?? undefined,
        },
        forwardedFor,
      }),
    ])

    const row = selectChampionSummaryRow(summaries, championId, query.position)

    return {
      championId,
      championName: staticData?.championName ?? null,
      championIconUrl: staticData?.championIconUrl ?? null,
      stats: row === null
        ? null
        : {
            position: row.position,
            tier: row.tier,
            winRate: row.winRate,
            pickRate: row.pickRate,
            banRate: row.banRate,
            games: row.games,
            patch: row.patchVersion,
            // Echo back what was asked rather than what the row carries: the
            // directory rows don't restate the filter they were computed under.
            eloBracket: query.eloBracket ?? 'ALL',
          },
    }
  },
  {
    maxAge: 60 * 60,
    name: 'og-champion-card',
    getKey: (championId: number, query: ChampionSliceQuery) =>
      [championId, query.patch ?? '', query.eloBracket ?? '', query.position ?? ''].join('-'),
  },
  (championId: number): ChampionOgCard => ({ championId, championName: null, championIconUrl: null, stats: null }),
)

export default defineEventHandler(async (event): Promise<ChampionOgCard> => {
  const championId = readChampionId(getRouterParam(event, 'championId'))
  if (championId === null) {
    throw createError({ statusCode: 400, statusMessage: 'Invalid championId' })
  }
  return loadChampionOgCard(championId, readChampionSliceQuery(event), visitorAddress(event))
})
