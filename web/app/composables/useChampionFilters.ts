import type { ChampionPosition } from '~/utils/positions'
import { DEFAULT_ELO_BRACKET, resolveEloBracket } from '~/utils/elo-brackets'
import { firstParamValue } from '~/utils/route-params'

/**
 * @param options.defaultEloBracket Bracket used when `?elo=` is absent. Master+
 * on the global champion pages (`DEFAULT_ELO_BRACKET`); the player-scoped
 * champion page passes `ELO_BRACKET_ALL`, because its scope is already one
 * account and re-slicing that by rank empties the build of any truemain below
 * Master.
 */
export function useChampionFilters(options: { defaultEloBracket?: string } = {}) {
  const route = useRoute()
  const router = useRouter()
  const defaultEloBracket = options.defaultEloBracket ?? DEFAULT_ELO_BRACKET

  const filters = computed(() => {
    // Always resolves to a concrete bracket — never `undefined`. The page
    // default is no longer the server's (`ALL`), so "no param" can't mean "send
    // no param": every consumer forwards this value explicitly, otherwise the
    // page would render Master+ in its header while fetching every tier. A
    // hand-typed / shared `?elo=gold` is upper-cased and honoured like the
    // backend does; junk falls back to the page default rather than widening to
    // every tier under a header that says Master+.
    const eloBracket = resolveEloBracket(firstParamValue(route.query.elo), defaultEloBracket)

    // `vs` holds the lane opponent (#923). Parsed to a positive int so a junk
    // value is dropped rather than forwarded to the API as garbage.
    const rawOpponent = Number.parseInt(firstParamValue(route.query.vs) ?? '', 10)
    const opponentChampionId = Number.isFinite(rawOpponent) && rawOpponent > 0 ? rawOpponent : undefined

    // Population filter (#1346). On by default — the site is about truemains,
    // and it is also the population every number here described before the
    // aggregate started carrying the rest. Only the opt-out is ever in the URL
    // (`?everyone=1`), so the resting URL stays clean like the bracket's.
    //
    // A pinned matchup forces it back on: matchups come from an aggregate whose
    // champion side is mains-only, and the API rejects the combination outright
    // rather than serving mains-only rows under an "everyone" label. Resolving
    // it here makes the invalid pair unrepresentable, so a hand-edited
    // `?vs=…&everyone=1` can't 400 the page — it just renders the matchup.
    const truemainsOnly = opponentChampionId !== undefined
      || firstParamValue(route.query.everyone) !== '1'

    return {
      patch: firstParamValue(route.query.patch) || undefined,
      position: firstParamValue(route.query.position) || undefined,
      eloBracket,
      truemainsOnly,
      opponentChampionId,
    }
  })

  // The default bracket is not a filter — it is the page's resting state, so
  // it must not light up the "filters active" affordances on an untouched page.
  const hasFilters = computed(() =>
    Boolean(filters.value.patch || filters.value.position
      || filters.value.eloBracket !== defaultEloBracket
      || !filters.value.truemainsOnly
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
    truemainsOnly?: boolean
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
      // Drop the param at the page default so the resting URL stays clean, and
      // pin every other bracket — `ALL` included, which is now a deliberate
      // choice rather than the absence of one.
      if (updates.eloBracket && updates.eloBracket !== defaultEloBracket) {
        nextQuery.elo = updates.eloBracket
      }
      else delete nextQuery.elo
    }

    if (updates.truemainsOnly !== undefined) {
      // Only the opt-out is written: `truemainsOnly` is the resting state.
      if (updates.truemainsOnly) delete nextQuery.everyone
      else nextQuery.everyone = '1'
    }

    if (updates.opponentChampionId !== undefined) {
      if (updates.opponentChampionId) nextQuery.vs = String(updates.opponentChampionId)
      else delete nextQuery.vs
    }

    await router.replace({ query: nextQuery })
  }

  return { filters, hasFilters, setFilter }
}
