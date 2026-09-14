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
 * **Only an absence is cached** (#1557). A 404 describes the slice — the champion
 * has no aggregate there — and is as true in five minutes as now. A 429, a 5xx or
 * an unreachable upstream describes the moment: it used to be folded into the
 * same empty summary and cached, so one throttled request blanked the paragraph
 * for every visitor of that slice for five minutes. Those now fail the cached
 * loader, which stores nothing, and the handler answers that one view with the
 * empty summary.
 *
 * **The fan-out is attributed to the visitor who caused it** (#1557). These are
 * in-process calls through the `/api` proxy, which carry no headers of their own:
 * without the visitor's `X-Forwarded-For` the API would key every summary on the
 * web container, one rate-limit bucket shared by the whole site. Only that header
 * is forwarded — the rest of the visitor's request has no business in a response
 * shared by everyone who hits the same cache key.
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
  /**
   * Population the prose describes (#1346). Mirrors the page's own toggle: the
   * summary sits under the build panels, so folding it from a different set of
   * players than they use would put two different builds on one screen.
   */
  truemainsOnly: boolean
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
    // Opt-out only, and anything that isn't the literal "false" means on — the
    // safe direction for a public, cache-keyed route.
    truemainsOnly: query.truemainsOnly !== 'false',
  }
}

/** The status of a failed upstream call, when it got as far as an HTTP answer. */
function upstreamStatus(error: unknown): number | undefined {
  const failure = error as { statusCode?: unknown, response?: { status?: unknown } } | null
  const status = failure?.statusCode ?? failure?.response?.status
  return typeof status === 'number' ? status : undefined
}

/** A 404 is the slice's answer and degrades to `null`; anything else is rethrown so it is never cached. */
function absentOnlyWhenNotFound(error: unknown): null {
  if (upstreamStatus(error) === 404) return null
  throw error
}

const loadChampionBuildSummary = defineCachedFunction<ChampionBuildSummary, [number, SummaryQuery, string?]>(
  async (championId: number, query: SummaryQuery, forwardedFor?: string): Promise<ChampionBuildSummary> => {
    const patch = query.patch ?? undefined
    const headers = forwardedFor ? { 'x-forwarded-for': forwardedFor } : undefined

    const [champion, championStatic, itemsMap, runeTree, summonersMap, opponentStatic] = await Promise.all([
      // A 404 here is meaningful rather than exceptional — the champion simply
      // has no aggregate for this slice — and `resolveChampionBuildSummary`
      // renders that as an empty summary, the same "no data" the page shows.
      $fetch<ChampionResponse>(`/api/champions/${championId}`, {
        query: {
          patch,
          position: query.position ?? undefined,
          eloBracket: query.eloBracket ?? undefined,
          truemainsOnly: query.truemainsOnly ? undefined : 'false',
          opponentChampionId: query.opponentChampionId ?? undefined,
        },
        headers,
      }).catch(absentOnlyWhenNotFound),
      $fetch<ChampionStaticData>(`/api/static/${championId}`, { query: { patch }, headers }).catch(absentOnlyWhenNotFound),
      $fetch<Record<number, StaticItemData>>('/api/static/items', { query: { patch }, headers }).catch(absentOnlyWhenNotFound),
      $fetch<RuneTreeResponse>('/api/static/rune-tree', { query: { patch }, headers }).catch(absentOnlyWhenNotFound),
      $fetch<Record<number, StaticSummonerSpellData>>('/api/static/summoner-spells', { query: { patch }, headers }).catch(absentOnlyWhenNotFound),
      // Only when one is pinned — the unfiltered page must not pay a sixth
      // lookup for a name it will never print.
      query.opponentChampionId === null
        ? Promise.resolve(null)
        : $fetch<ChampionStaticData>(`/api/static/${query.opponentChampionId}`, { query: { patch }, headers }).catch(absentOnlyWhenNotFound),
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
)

export default defineEventHandler(async (event): Promise<ChampionBuildSummary> => {
  const championId = readChampionId(getRouterParam(event, 'championId'))
  if (championId === null) {
    throw createError({ statusCode: 400, statusMessage: 'Invalid championId' })
  }
  const query = readQuery(event)
  try {
    return await loadChampionBuildSummary(championId, query, getRequestHeader(event, 'x-forwarded-for'))
  }
  catch {
    // An upstream said "not now" (429, 5xx, unreachable). The loader stored
    // nothing, so the next view retries; this one gets the empty summary rather
    // than a second fan-out against an API that just asked for less traffic.
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
})
