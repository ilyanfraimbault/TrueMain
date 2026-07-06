import type { ChampionPosition } from '~/utils/positions'
import { normalizeEloBracket, ELO_BRACKET_ALL } from '~/utils/elo-brackets'

function getQuery(value: string | string[] | undefined): string {
  return Array.isArray(value) ? value[0] ?? '' : value ?? ''
}

export function useChampionFilters() {
  const route = useRoute()
  const router = useRouter()

  const filters = computed(() => {
    // Only forward a recognised, non-default bracket. Normalise first (upper-
    // cases + validates) so a hand-typed / shared `?elo=gold` is honoured like
    // the backend does, instead of being silently dropped. `ALL` (and any junk)
    // is the server default, so it maps to `undefined` — keeping the query, and
    // the data cache key, identical to an unfiltered request.
    const rawBracket = getQuery(route.query.elo as string | string[] | undefined)
    const normalizedBracket = normalizeEloBracket(rawBracket)
    const eloBracket = normalizedBracket === ELO_BRACKET_ALL ? undefined : normalizedBracket

    return {
      patch: getQuery(route.query.patch as string | string[] | undefined) || undefined,
      position: getQuery(route.query.position as string | string[] | undefined) || undefined,
      eloBracket,
    }
  })

  const hasFilters = computed(() =>
    Boolean(filters.value.patch || filters.value.position || filters.value.eloBracket),
  )

  // `undefined` = leave the field alone, `null` = clear it, string = set it.
  // Mirrors the `applyFilterReset` pattern used on /champions so the two
  // pages handle filter clearing the same way.
  async function setFilter(updates: {
    patch?: string | null
    position?: ChampionPosition | null
    eloBracket?: string | null
  }) {
    const nextQuery: Record<string, string> = {}
    for (const [key, value] of Object.entries(route.query)) {
      if (typeof value === 'string') nextQuery[key] = value
    }

    if (updates.patch !== undefined) {
      if (updates.patch) nextQuery.patch = updates.patch
      else delete nextQuery.patch
    }
    if (updates.position !== undefined) {
      if (updates.position) nextQuery.position = updates.position
      else delete nextQuery.position
    }
    if (updates.eloBracket !== undefined) {
      // `ALL` is the default, so clear the param rather than pin it.
      if (updates.eloBracket && updates.eloBracket !== ELO_BRACKET_ALL) nextQuery.elo = updates.eloBracket
      else delete nextQuery.elo
    }

    await router.replace({ query: nextQuery })
  }

  return { filters, hasFilters, setFilter }
}
