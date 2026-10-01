/**
 * Which population the champion pages fold their numbers from (#1346): the
 * champion's *mains* — the site's truemains, and the only population the
 * aggregate held before #1346 — or every tracked player with games on it.
 *
 * Extracted from `useChampionFilters` so the rule can be tested without a Nuxt
 * context, the same way `champion-fetch`'s fallback is.
 */

/** URL param carrying the opt-out. Only ever written when the toggle is off. */
export const EVERYONE_QUERY_PARAM = 'everyone'

/** Value that turns the toggle off; anything else reads as on. */
export const EVERYONE_QUERY_VALUE = '1'

/**
 * Resolves the population from the URL.
 *
 * On by default: truemains are the site's thesis, and they are also the
 * population every number on these pages described before the aggregate started
 * carrying the rest — so the absence of a param has to mean the pre-existing
 * answer.
 *
 * A pinned matchup forces it back on, whatever the param says. Matchups are
 * folded from an aggregate whose champion side is mains-only, so "everyone" is
 * not an answer that slice can give, and the API rejects the pair outright.
 * Resolving it here makes the invalid combination unrepresentable: a
 * hand-edited or shared `?vs=…&everyone=1` renders the matchup instead of
 * 400-ing the page.
 *
 * @param everyoneParam Raw `?everyone=` value, or null/undefined when absent.
 * @param opponentChampionId Pinned lane opponent, or undefined when none.
 */
export function resolveTruemainsOnly(
  everyoneParam: string | null | undefined,
  opponentChampionId: number | undefined,
): boolean {
  if (opponentChampionId !== undefined) return true
  return everyoneParam !== EVERYONE_QUERY_VALUE
}
