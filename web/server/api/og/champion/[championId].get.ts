import type { H3Event } from 'h3'
import type { ChampionSummaryResponse } from '~~/shared/types/champions'
import type { ChampionOgCard } from '~~/shared/types/og-card'
import type { ChampionStaticData } from '~~/shared/types/static-data'
import { parseOgChampionId, selectChampionSummaryRow } from '~~/shared/utils/og-card'

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

// Shape guards on the query, not semantic validation: an unknown position or a
// nonsense elo bracket simply fails to match a row and degrades the card. The
// point here is to bound the *cache key* space, since this route is publicly
// reachable and each distinct key costs one backend call.
const PATCH_RE = /^\d{1,3}\.\d{1,3}$/
const ELO_BRACKET_RE = /^[A-Z_]{1,20}$/
const POSITION_RE = /^[A-Z]{1,10}$/

interface ChampionOgQuery {
  patch: string | null
  eloBracket: string | null
  position: string | null
}

function readQuery(event: H3Event): ChampionOgQuery {
  const query = getQuery(event) as Record<string, unknown>
  const pick = (key: string, pattern: RegExp): string | null => {
    const raw = query[key]
    if (typeof raw !== 'string') return null
    const value = raw.trim().toUpperCase()
    return pattern.test(value) ? value : null
  }
  return {
    // The patch is the one value that isn't upper-cased meaningfully, but
    // digits and a dot survive `toUpperCase()` unchanged.
    patch: pick('patch', PATCH_RE),
    eloBracket: pick('eloBracket', ELO_BRACKET_RE),
    position: pick('position', POSITION_RE),
  }
}

/**
 * Resolves the card. Both upstreams are independent and both degrade on their
 * own: a DDragon outage costs the portrait and the name, a backend outage costs
 * the stats block, and neither substitutes for the other. That is the whole
 * fallback contract — the template renders the branded card when both are gone.
 */
const loadChampionOgCard = defineCachedFunction(
  async (championId: number, query: ChampionOgQuery): Promise<ChampionOgCard> => {
    const [staticData, summaries] = await Promise.all([
      $fetch<ChampionStaticData>(`/api/static/${championId}`).catch(() => null),
      $fetch<ChampionSummaryResponse[]>('/api/champions', {
        query: {
          patch: query.patch ?? undefined,
          eloBracket: query.eloBracket ?? undefined,
        },
      }).catch(() => null),
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
    getKey: (championId: number, query: ChampionOgQuery) =>
      [championId, query.patch ?? '', query.eloBracket ?? '', query.position ?? ''].join('-'),
  },
)

export default defineEventHandler(async (event): Promise<ChampionOgCard> => {
  const championId = parseOgChampionId(getRouterParam(event, 'championId'))
  if (championId === null) {
    throw createError({ statusCode: 400, statusMessage: 'Invalid championId' })
  }
  return loadChampionOgCard(championId, readQuery(event))
})
