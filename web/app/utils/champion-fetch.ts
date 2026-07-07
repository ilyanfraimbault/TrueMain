import type { ChampionResponse } from '~~/shared/types/champions'
import { fetchErrorStatus } from '~/utils/errors'

/**
 * The slice the page requests — the page filters the global champion endpoint
 * accepts as query params. `patch` / `position` are auto-resolvable (the API
 * resolves the dominant one when unset), so the 404 fallback drops them; the
 * explicit `eloBracket` rank filter is preserved instead (see
 * {@link resolveGlobalChampion}).
 */
export interface ChampionSliceFilters {
  patch?: string
  position?: string
  eloBracket?: string
}

export interface GlobalChampionOutcome {
  /**
   * The resolved slice, or `null` when we hold no aggregate for the requested
   * slice — either the champion has no games at all (brand-new, or nobody in the
   * dataset plays it) or the picked rank has none. The caller treats
   * `data === null` on a settled fetch as the "no data" state.
   */
  data: ChampionResponse | null
  /**
   * The champion's default (unfiltered) slice, fetched via the fallback path
   * when a pinned patch/position 404'd. Non-null only on that path — the caller
   * stashes it under the unfiltered cache key so a later key flip reuses it
   * instead of refetching. `null` on every other path.
   */
  fallbackData: ChampionResponse | null
}

/**
 * Runs the global champion fetch and classifies its result. A 404 is treated as
 * "no data" (`data: null`) rather than an error: when a patch/position is pinned
 * we first retry dropping only those (the API resolves the dominant patch /
 * position) — a 404 there usually just means that one patch/position is empty —
 * and only conclude "no data" when even that retry 404s. Every non-404 failure
 * (429, 500, network) propagates so the caller can surface it.
 *
 * The retry deliberately KEEPS the rank filter (`eloBracket`): it is the user's
 * explicit choice, not an auto-resolvable default, so dropping it would silently
 * show all-ranks data under the picked rank. When a rank genuinely has no games
 * the retry (still carrying the rank) 404s again → `data: null`, and the page
 * renders a "no games in this rank" state rather than a misleading ALL slice.
 * A request with only a rank set (no patch/position to drop) skips the retry and
 * resolves straight to `data: null`.
 *
 * Pure over its injected `fetcher`, so the control flow is unit-testable without
 * the Nuxt runtime. The composable wires `fetcher` to `$fetch('/api/champions/
 * {id}')` and stashes `fallbackData` (when present) under its cache key.
 *
 * @param fetcher issues the request; called with the active filters, then again
 *   with only the rank preserved (patch / position dropped) for the fallback.
 * @param filters the active patch / position / rank filters.
 */
export async function resolveGlobalChampion(
  fetcher: (query?: ChampionSliceFilters) => Promise<ChampionResponse>,
  filters: ChampionSliceFilters,
): Promise<GlobalChampionOutcome> {
  try {
    return { data: await fetcher(filters), fallbackData: null }
  }
  catch (error: unknown) {
    // Anything other than a 404 (429, 500, network, problem responses) is a
    // real failure and must propagate so the page can surface it.
    if (fetchErrorStatus(error) !== 404) throw error

    // Only patch / position are auto-resolvable, so only they get dropped;
    // `preservedFilters` carries the explicit rank filter (and any future
    // explicit filter) into the retry.
    const { patch, position, ...preservedFilters } = filters
    const hadResolvableFilters = Boolean(patch || position)
    if (hadResolvableFilters) {
      // A 404 on a patch/position slice usually just means that slice is empty —
      // retry at the default patch/position (rank kept) before giving up.
      try {
        const fallback = await fetcher(preservedFilters)
        return { data: fallback, fallbackData: fallback }
      }
      catch (fallbackError: unknown) {
        if (fetchErrorStatus(fallbackError) !== 404) throw fallbackError
        // Even the rank-only slice 404s: no data for this rank (or champion).
        // Fall through to the no-data outcome.
      }
    }

    // A 404 with nothing left to drop (only a rank, or no filters), or whose
    // retry also 404s, means we genuinely have no data for this slice — an empty
    // state, not an error.
    return { data: null, fallbackData: null }
  }
}
