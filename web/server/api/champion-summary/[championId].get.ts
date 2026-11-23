import type { H3Event } from 'h3'
import type { ChampionBuildSummary } from '~~/shared/types/champion-build-summary'
import type { ChampionMatchupsResponse, ChampionResponse } from '#shared/contract/api'
import type {
  ChampionStaticData,
  ChampionStaticListItem,
  RuneTreeResponse,
  StaticItemData,
  StaticSummonerSpellData,
} from '~~/shared/types/static-data'
import { resolveChampionBuildSummary } from '~~/shared/utils/champion-build-summary'
import { resolveSummaryMatchups } from '~~/shared/utils/champion-matchup-summary'
import { readChampionId, readChampionSliceQuery, type ChampionSliceQuery } from '~~/server/utils/champion-slice-query'
import { defineCachedRead, readOrAbsent, visitorAddress } from '~~/server/utils/upstream'

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
 * **Cost.** The whole fan-out sits behind a `defineCachedFunction` keyed on the
 * slice, so a champion page view costs a backend round-trip once per
 * `SUMMARY_CACHE_SECONDS` per (champion, position, patch, rank) — not once per
 * view. That is what makes the trade-off in `decisions.md` affordable; without
 * the cache this would be a backend hit on every page view, which is precisely
 * what #926 declined to pay.
 *
 * Every upstream degrades on its own: no backend means no numbers, no DDragon
 * means no names, and the block simply renders less. None substitutes for
 * another, and nothing is invented — see `resolveChampionBuildSummary`.
 *
 * **The lane's matchups ride along** (#1954): the Matchups panel is client-only,
 * so without them the server HTML held its "no matchups" empty state. They are
 * read *after* the champion, on the lane and patch it resolved — the panel's own
 * scope, which an unfiltered request does not know up front, and a matchup read
 * without a patch spans every patch the aggregate still holds. One more call per
 * cache miss, not per view; skipped when an opponent is pinned.
 *
 * Only an absence is cached, and the fan-out is attributed to the visitor who
 * caused it: `server/utils/upstream.ts` (#1557). A throttled or failing upstream
 * answers this one view with the empty summary.
 */

/**
 * How long a resolved summary may be served for (#1273).
 *
 * Was an hour, which put this paragraph in open contradiction with the page
 * around it: every panel on `/champions/{slug}` fetches client-side and live,
 * so the prose printed the slice as it stood up to an hour ago under a header
 * and a patch picker showing the current one — "Across 7 ranked games on patch
 * 16.17" under a header reading 24 games, or "on patch 16.16" under a picker
 * reading 16.17. Same endpoint, same field, two different ages.
 *
 * A patch roll is the case the key cannot rescue on its own: an unfiltered
 * request keys on an *empty* patch while its answer depends on the patch the
 * backend resolves, so the entry has no way to notice that the backend has
 * moved on. Learning the resolved patch before the call would cost the very
 * round-trip the cache exists to avoid, so the honest fix is to make the window
 * short enough that no one reads a dead patch number for long — this paragraph
 * is the only build content in the server-rendered HTML (#1123), so a stale
 * patch is also what a crawler indexes.
 *
 * Five minutes still collapses the page-view burst the cache was added for
 * (#926's objection was a backend hit *per view*, not per five minutes), and it
 * tracks the ingestor closely enough that the "below the sample TrueMain
 * requires" caveat stops flickering on and off early in a patch, when a sample
 * can triple inside the old window.
 */
const SUMMARY_CACHE_SECONDS = 5 * 60

interface SummaryQuery extends ChampionSliceQuery {
  /**
   * Lane opponent (#923's `?vs=`). Not optional polish: with one pinned, the
   * page's panels are re-sliced to the games the two champions actually met in,
   * so a summary that ignored it would describe the champion's *global* build in
   * prose directly under panels showing a different one.
   */
  opponentChampionId: number | null
  /**
   * Population the prose describes (#1346). Mirrors the page's own toggle: the
   * summary sits under the build panels, so folding it from a different set of
   * players than they use would put two different builds on one screen.
   */
  truemainsOnly: boolean
}

function readSummaryQuery(event: H3Event): SummaryQuery {
  const query = getQuery(event) as Record<string, unknown>
  return {
    ...readChampionSliceQuery(event),
    opponentChampionId: readChampionId(
      typeof query.opponentChampionId === 'string' ? query.opponentChampionId.trim() : undefined,
    ),
    // Opt-out only, and anything that isn't the literal "false" means on — the
    // safe direction for a public, cache-keyed route.
    truemainsOnly: query.truemainsOnly !== 'false',
  }
}

function emptySummary(championId: number, query: SummaryQuery): ChampionBuildSummary {
  return resolveChampionBuildSummary({
    championId,
    champion: null,
    championStatic: null,
    itemsMap: null,
    runeTree: null,
    summonersMap: null,
    requestedEloBracket: query.eloBracket ?? 'ALL',
    opponentName: null,
    opponentIconUrl: null,
  })
}

const loadChampionBuildSummary = defineCachedRead<ChampionBuildSummary, [number, SummaryQuery, string?]>(
  async (championId: number, query: SummaryQuery, forwardedFor?: string): Promise<ChampionBuildSummary> => {
    const patch = query.patch ?? undefined

    // A 404 here is meaningful rather than exceptional — the champion simply
    // has no aggregate for this slice — and `resolveChampionBuildSummary`
    // renders that as an empty summary, the same "no data" the page shows.
    const championFetch = readOrAbsent<ChampionResponse>(`/api/champions/${championId}`, {
      query: {
        patch,
        position: query.position ?? undefined,
        eloBracket: query.eloBracket ?? undefined,
        truemainsOnly: query.truemainsOnly ? undefined : 'false',
        opponentChampionId: query.opponentChampionId ?? undefined,
      },
      forwardedFor,
    })
    const withMatchups = query.opponentChampionId === null

    const [champion, championStatic, itemsMap, runeTree, summonersMap, opponentStatic, matchups, champions] = await Promise.all([
      championFetch,
      readOrAbsent<ChampionStaticData>(`/api/static/${championId}`, { query: { patch }, forwardedFor }),
      readOrAbsent<Record<number, StaticItemData>>('/api/static/items', { query: { patch }, forwardedFor }),
      readOrAbsent<RuneTreeResponse>('/api/static/rune-tree', { query: { patch }, forwardedFor }),
      readOrAbsent<Record<number, StaticSummonerSpellData>>('/api/static/summoner-spells', { query: { patch }, forwardedFor }),
      // Only when one is pinned — the unfiltered page must not pay a sixth
      // lookup for a name it will never print.
      query.opponentChampionId === null
        ? Promise.resolve(null)
        : readOrAbsent<ChampionStaticData>(`/api/static/${query.opponentChampionId}`, { query: { patch }, forwardedFor }),
      // The panel's exact query: resolved lane and patch, the page's bracket.
      // No lane resolved means no matchup to read — the endpoint 400s without one.
      withMatchups
        ? championFetch.then(resolved => resolved?.position
          ? readOrAbsent<ChampionMatchupsResponse>(`/api/champions/${championId}/matchups`, {
              query: { position: resolved.position, patch: resolved.patch, eloBracket: query.eloBracket ?? undefined },
              forwardedFor,
            })
          : null)
        : Promise.resolve(null),
      withMatchups
        ? readOrAbsent<ChampionStaticListItem[]>('/api/static/champions', { query: { patch }, forwardedFor })
        : Promise.resolve(null),
    ])

    const summary = resolveChampionBuildSummary({
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
    const named = resolveSummaryMatchups(matchups?.matchups, champions)
    return named ? { ...summary, matchups: named } : summary
  },
  {
    maxAge: SUMMARY_CACHE_SECONDS,
    name: 'champion-build-summary',
    getKey: (championId: number, query: SummaryQuery) => [
      championId,
      query.patch ?? '',
      query.eloBracket ?? '',
      query.position ?? '',
      query.opponentChampionId ?? '',
      query.truemainsOnly ? 'truemains' : 'everyone',
    ].join('-'),
  },
  emptySummary,
)

export default defineEventHandler(async (event): Promise<ChampionBuildSummary> => {
  const championId = readChampionId(getRouterParam(event, 'championId'))
  if (championId === null) {
    throw createError({ statusCode: 400, statusMessage: 'Invalid championId' })
  }
  return loadChampionBuildSummary(championId, readSummaryQuery(event), visitorAddress(event))
})
