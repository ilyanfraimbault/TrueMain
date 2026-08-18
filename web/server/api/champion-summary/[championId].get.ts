import type { H3Event } from 'h3'
import type { ChampionBuildSummary } from '~~/shared/types/champion-build-summary'
import type { ChampionResponse } from '~~/shared/types/champions'
import type {
  ChampionStaticData,
  RuneTreeResponse,
  StaticItemData,
  StaticSummonerSpellData,
} from '~~/shared/types/static-data'
import { resolveChampionBuildSummary } from '~~/shared/utils/champion-build-summary'

/**
 * The champion page's build, resolved to names, for server-side rendering
 * (#1123).
 *
 * Why the page can't just SSR its own fetches: naming an item needs
 * `/api/static/items`, ~373 KiB of descriptions and icons that `useStaticItems`
 * keeps client-only on purpose. SSR-ing that map would inline the blob into the
 * HTML of every champion page to print a dozen words. So the ids are resolved
 * *here*, on the server, and only the words travel: the names, plus one icon
 * URL per named entity so the paragraph can mark them inline (#1143). Around
 * 3 KB, 844 B gzipped, against the ~373 KiB of the map they came from.
 *
 * Shaped exactly like `server/api/og/champion/[championId].get.ts`, and for the
 * same reason: a purpose-built, cached read model that resolves its own slice
 * rather than being handed one by a page that doesn't have it.
 *
 * **Cost.** The whole fan-out sits behind a 1 h `defineCachedFunction` keyed on
 * the slice, so a champion page view costs a backend round-trip once per hour
 * per (champion, position, patch, rank) — not once per view. That is what makes
 * the trade-off in `decisions.md` affordable; without the cache this would be a
 * backend hit on every page view, which is precisely what #926 declined to pay.
 *
 * Every upstream degrades on its own: no backend means no numbers, no DDragon
 * means no names, and the block simply renders less. None substitutes for
 * another, and nothing is invented — see `resolveChampionBuildSummary`.
 */

// Shape guards, not semantic validation: an unknown lane or a nonsense bracket
// fails to match a slice and degrades the block. Their real job is bounding the
// *cache key* space, since this route is publicly reachable and each distinct
// key costs one backend call.
const PATCH_RE = /^\d{1,3}\.\d{1,3}$/
const ELO_BRACKET_RE = /^[A-Z_]{1,20}$/
const POSITION_RE = /^[A-Z]{1,10}$/

interface SummaryQuery {
  patch: string | null
  eloBracket: string | null
  position: string | null
  /**
   * Lane opponent (#923's `?vs=`). Not optional polish: with one pinned, the
   * page's panels are re-sliced to the games the two champions actually met in,
   * so a summary that ignored it would describe the champion's *global* build in
   * prose directly under panels showing a different one.
   */
  opponentChampionId: number | null
}

function readChampionId(raw: string | undefined): number | null {
  // Same bound as the OG endpoint's `championId` guard, and for the same reason
  // the query patterns exist: this route is publicly reachable and every
  // distinct key is a cache entry plus a backend call.
  if (!raw || !/^\d{1,7}$/.test(raw)) return null
  const value = Number(raw)
  return value > 0 ? value : null
}

function readQuery(event: H3Event): SummaryQuery {
  const query = getQuery(event) as Record<string, unknown>
  const pick = (key: string, pattern: RegExp): string | null => {
    const raw = query[key]
    if (typeof raw !== 'string') return null
    const value = raw.trim().toUpperCase()
    return pattern.test(value) ? value : null
  }
  return {
    // Digits and a dot survive `toUpperCase()` unchanged.
    patch: pick('patch', PATCH_RE),
    eloBracket: pick('eloBracket', ELO_BRACKET_RE),
    position: pick('position', POSITION_RE),
    opponentChampionId: readChampionId(
      typeof query.opponentChampionId === 'string' ? query.opponentChampionId.trim() : undefined,
    ),
  }
}

const loadChampionBuildSummary = defineCachedFunction(
  async (championId: number, query: SummaryQuery): Promise<ChampionBuildSummary> => {
    const patch = query.patch ?? undefined

    const [champion, championStatic, itemsMap, runeTree, summonersMap, opponentStatic] = await Promise.all([
      // A 404 here is meaningful rather than exceptional — the champion simply
      // has no aggregate for this slice — and `resolveChampionBuildSummary`
      // renders that as an empty summary, the same "no data" the page shows.
      $fetch<ChampionResponse>(`/api/champions/${championId}`, {
        query: {
          patch,
          position: query.position ?? undefined,
          eloBracket: query.eloBracket ?? undefined,
          opponentChampionId: query.opponentChampionId ?? undefined,
        },
      }).catch(() => null),
      $fetch<ChampionStaticData>(`/api/static/${championId}`, { query: { patch } }).catch(() => null),
      $fetch<Record<number, StaticItemData>>('/api/static/items', { query: { patch } }).catch(() => null),
      $fetch<RuneTreeResponse>('/api/static/rune-tree', { query: { patch } }).catch(() => null),
      $fetch<Record<number, StaticSummonerSpellData>>('/api/static/summoner-spells', { query: { patch } }).catch(() => null),
      // Only when one is pinned — the unfiltered page must not pay a sixth
      // lookup for a name it will never print.
      query.opponentChampionId === null
        ? Promise.resolve(null)
        : $fetch<ChampionStaticData>(`/api/static/${query.opponentChampionId}`, { query: { patch } }).catch(() => null),
    ])

    return resolveChampionBuildSummary({
      championId,
      champion,
      championStatic,
      itemsMap,
      runeTree,
      summonersMap,
      requestedEloBracket: query.eloBracket ?? 'ALL',
      opponentName: opponentStatic?.championName ?? null,
      opponentIconUrl: opponentStatic?.championIconUrl ?? null,
    })
  },
  {
    maxAge: 60 * 60,
    name: 'champion-build-summary',
    getKey: (championId: number, query: SummaryQuery) => [
      championId,
      query.patch ?? '',
      query.eloBracket ?? '',
      query.position ?? '',
      query.opponentChampionId ?? '',
    ].join('-'),
  },
)

export default defineEventHandler(async (event): Promise<ChampionBuildSummary> => {
  const championId = readChampionId(getRouterParam(event, 'championId'))
  if (championId === null) {
    throw createError({ statusCode: 400, statusMessage: 'Invalid championId' })
  }
  return loadChampionBuildSummary(championId, readQuery(event))
})
