import type { ChampionResponse } from '~~/shared/types/champions'
import { fetchErrorStatus } from '~/utils/errors'

/**
 * The patch/position slice the page requests — the subset of the page filters
 * the global champion endpoint accepts as query params. The composable forwards
 * the whole filters object at runtime, so an added filter still reaches the API;
 * widen this interface when one should be type-visible here too.
 */
export interface ChampionSliceFilters {
  patch?: string
  position?: string
}

export interface GlobalChampionOutcome {
  /**
   * The resolved slice, or `null` when we hold no aggregate for the champion at
   * all (a brand-new champion, or one nobody in the dataset has played). The
   * caller treats `data === null` on a settled fetch as the "no data" state.
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
 * we first retry the champion's default (unfiltered) slice — a 404 there usually
 * just means that one slice is empty — and only conclude "no data" when even the
 * unfiltered fetch 404s. Every non-404 failure (429, 500, network) propagates so
 * the caller can surface it.
 *
 * Pure over its injected `fetcher`, so the control flow is unit-testable without
 * the Nuxt runtime. The composable wires `fetcher` to `$fetch('/api/champions/
 * {id}')` and stashes `fallbackData` (when present) under its cache key.
 *
 * @param fetcher issues the request; called with the active filters, then again
 *   with no query for the unfiltered fallback.
 * @param filters the active patch/position filters.
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

    const hadFilters = Boolean(filters.patch || filters.position)
    if (hadFilters) {
      // A 404 on a filtered slice usually just means that patch/position is
      // empty — retry the champion's default slice before concluding there's
      // no data at all.
      try {
        const fallback = await fetcher()
        return { data: fallback, fallbackData: fallback }
      }
      catch (fallbackError: unknown) {
        if (fetchErrorStatus(fallbackError) !== 404) throw fallbackError
        // Even the unfiltered fetch 404s: we hold no aggregate for this
        // champion at all. Fall through to the no-data outcome.
      }
    }

    // A 404 with no filters (or whose unfiltered fallback also 404s) means we
    // genuinely have no data on this champion yet — an empty state, not an
    // error.
    return { data: null, fallbackData: null }
  }
}
