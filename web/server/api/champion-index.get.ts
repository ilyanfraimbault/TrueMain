import type { H3Event } from 'h3'
import type { ChampionIndexResponse } from '~~/shared/types/champion-index'
import type { ChampionTierListResponse } from '~~/shared/types/champions'
import type { ChampionStaticListItem } from '~~/shared/types/static-data'
import {
  championIndexLinks,
  championIndexPatch,
  championIndexTiers,
  emptyChampionIndex,
} from '~~/shared/utils/champion-index'

/**
 * The champion link graph, resolved to names, for server-side rendering (#1209).
 *
 * Why the pages can't just SSR the fetches they already have: `/champions` and
 * `/champions/tierlist` need the champion *names*, and the only source of those
 * is `/api/static/champions` — ~20 kB of names **and CDN icon URLs**, which
 * `useChampionStaticList` keeps client-only. SSR-ing it would inline that blob
 * into the HTML of the site's three busiest pages to print a list of words. So
 * the ids are resolved here, on the server, and only the words travel: measured
 * on the built app, 6.0 kB for the full A→Z index (1.4 kB gzipped), 9.5 kB for
 * the tier view (1.7 kB) and 715 B for the homepage's twelve (260 B).
 *
 * Shaped exactly like `server/api/champion-summary/[championId].get.ts`, and for
 * the same reason: a purpose-built, cached read model that resolves its own
 * slice rather than being handed one by a page that doesn't have it.
 *
 * **Cost.** Behind a 1 h `defineCachedFunction` keyed on the view and its
 * filters, so a page view costs a backend round-trip once per hour per slice,
 * not once per view — the condition #1123 set for paying an SSR round-trip at
 * all. The `all` view costs *no* backend call: it reads DDragon through
 * `/api/static/champions`, which is itself already cached for an hour.
 */

// Shape guards, not semantic validation — their real job is bounding the cache
// key space, since this route is publicly reachable and each distinct key costs
// one backend call. Same patterns as the champion-summary endpoint.
const PATCH_RE = /^\d{1,3}\.\d{1,3}$/
const ELO_BRACKET_RE = /^[A-Z_]{1,20}$/
const POSITION_RE = /^[A-Z]{1,10}$/

/**
 * `all` — every live champion A→Z, for the directory's index block and the
 * champion page's cross-links. No ranking, no backend, no filters.
 * `tiers` — the tier list as named links, mirroring the filters of the page
 * that renders it.
 */
type ChampionIndexView = 'all' | 'tiers'

/** Bounds the `limit` key space; the homepage asks for 12. */
const MAX_LIMIT = 50

interface IndexQuery {
  view: ChampionIndexView
  patch: string | null
  eloBracket: string | null
  position: string | null
  limit: number | null
}

function readQuery(event: H3Event): IndexQuery {
  const query = getQuery(event) as Record<string, unknown>
  const pick = (key: string, pattern: RegExp): string | null => {
    const raw = query[key]
    if (typeof raw !== 'string') return null
    const value = raw.trim().toUpperCase()
    return pattern.test(value) ? value : null
  }

  const view: ChampionIndexView = query.view === 'tiers' ? 'tiers' : 'all'

  // The `all` view reads none of the filters, so it must not carry them: this
  // route is publicly reachable, and keeping them would let
  // `?view=all&patch=16.1` … `?view=all&patch=99.9` mint an unbounded family of
  // cache entries holding byte-identical answers. Normalising here rather than
  // in `getKey` keeps the key and what the function actually reads in one place.
  if (view === 'all') {
    return { view, patch: null, eloBracket: null, position: null, limit: null }
  }

  const rawLimit = typeof query.limit === 'string' ? Number(query.limit) : Number.NaN
  return {
    view,
    // Digits and a dot survive `toUpperCase()` unchanged.
    patch: pick('patch', PATCH_RE),
    eloBracket: pick('eloBracket', ELO_BRACKET_RE),
    position: pick('position', POSITION_RE),
    limit: Number.isInteger(rawLimit) && rawLimit > 0 ? Math.min(rawLimit, MAX_LIMIT) : null,
  }
}

const loadChampionIndex = defineCachedFunction(
  async (query: IndexQuery): Promise<ChampionIndexResponse> => {
    // Names come from the latest DDragon patch on both views, deliberately not
    // from `query.patch`: only the *names* are read out of this list and a
    // champion's name does not change with the patch, so scoping it would
    // fragment the cache (and the upstream's) for an identical answer.
    const staticList = await $fetch<ChampionStaticListItem[]>('/api/static/champions')
      .catch(() => null)

    if (query.view === 'all') {
      return { patch: null, champions: championIndexLinks(staticList), tiers: [] }
    }

    const tierList = await $fetch<ChampionTierListResponse>('/api/champions/tierlist', {
      query: {
        patch: query.patch ?? undefined,
        position: query.position ?? undefined,
        eloBracket: query.eloBracket ?? undefined,
      },
      // A 404/empty slice is meaningful rather than exceptional — the filters
      // simply match nothing — and renders as no block at all, the same "no
      // data" the chips above it show.
    }).catch(() => null)

    return {
      patch: championIndexPatch(tierList),
      champions: [],
      tiers: championIndexTiers(tierList, staticList, query.limit),
    }
  },
  {
    maxAge: 60 * 60,
    name: 'champion-index',
    getKey: (query: IndexQuery) => [
      query.view,
      query.patch ?? '',
      query.eloBracket ?? '',
      query.position ?? '',
      query.limit ?? '',
    ].join('-'),
  },
)

export default defineEventHandler(async (event): Promise<ChampionIndexResponse> => {
  // Both upstreams already degrade to `null` inside the cached function; this
  // catches the cache layer itself failing, so a page never 500s over a block
  // that is, by design, supplementary to what is already on it.
  return loadChampionIndex(readQuery(event)).catch(() => emptyChampionIndex())
})
