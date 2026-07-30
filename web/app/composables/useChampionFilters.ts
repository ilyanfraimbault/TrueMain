import type { ChampionPosition } from '~/utils/positions'
import { normalizeEloBracket, ELO_BRACKET_ALL } from '~/utils/elo-brackets'
import { firstParamValue } from '~/utils/route-params'

export function useChampionFilters() {
  const route = useRoute()
  const router = useRouter()

  const filters = computed(() => {
    // Only forward a recognised, non-default bracket. Normalise first (upper-
    // cases + validates) so a hand-typed / shared `?elo=gold` is honoured like
    // the backend does, instead of being silently dropped. `ALL` (and any junk)
    // is the server default, so it maps to `undefined` — keeping the query, and
    // the data cache key, identical to an unfiltered request.
    const rawBracket = firstParamValue(route.query.elo) ?? ''
    const normalizedBracket = normalizeEloBracket(rawBracket)
    const eloBracket = normalizedBracket === ELO_BRACKET_ALL ? undefined : normalizedBracket

    // `vs` holds the lane opponent (#923). Parsed to a positive int so a junk
    // value is dropped rather than forwarded to the API as garbage.
    const rawOpponent = Number.parseInt(firstParamValue(route.query.vs) ?? '', 10)
    const opponentChampionId = Number.isFinite(rawOpponent) && rawOpponent > 0 ? rawOpponent : undefined

    return {
      patch: firstParamValue(route.query.patch) || undefined,
      position: firstParamValue(route.query.position) || undefined,
      eloBracket,
      opponentChampionId,
    }
  })

  const hasFilters = computed(() =>
    Boolean(filters.value.patch || filters.value.position || filters.value.eloBracket
      || filters.value.opponentChampionId),
  )

  // `undefined` = leave the field alone, `null` = clear it, string/number =
  // set it. Pass `resetPage: true` on paginated pages so a filter change
  // drops `?page=` and anchors back on page 1 — otherwise switching from a
  // 5-page result to a single-page one leaves `?page=4` in the URL and the
  // list silently renders empty. All params transition in a single
  // router.replace so the URL updates atomically.
  async function setFilter(updates: {
    patch?: string | null
    position?: ChampionPosition | null
    championId?: number | null
    eloBracket?: string | null
    opponentChampionId?: number | null
  }, options: { resetPage?: boolean } = {}) {
    const nextQuery: Record<string, string> = {}
    for (const [key, value] of Object.entries(route.query)) {
      if (typeof value === 'string') nextQuery[key] = value
    }
    if (options.resetPage) delete nextQuery.page

    if (updates.patch !== undefined) {
      if (updates.patch) nextQuery.patch = updates.patch
      else delete nextQuery.patch
    }
    if (updates.position !== undefined) {
      if (updates.position) nextQuery.position = updates.position
      else delete nextQuery.position
    }
    if (updates.championId !== undefined) {
      if (updates.championId) nextQuery.championId = String(updates.championId)
      else delete nextQuery.championId
    }
    if (updates.eloBracket !== undefined) {
      // `ALL` is the default, so clear the param rather than pin it.
      if (updates.eloBracket && updates.eloBracket !== ELO_BRACKET_ALL) nextQuery.elo = updates.eloBracket
      else delete nextQuery.elo
    }

    if (updates.opponentChampionId !== undefined) {
      if (updates.opponentChampionId) nextQuery.vs = String(updates.opponentChampionId)
      else delete nextQuery.vs
    }

    await router.replace({ query: nextQuery })
  }

  return { filters, hasFilters, setFilter }
}
