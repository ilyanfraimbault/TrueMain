import type { CompositionBuildRequest } from '#shared/types/composition'

/**
 * Whether a composition request carries draft picks beyond the matchup itself —
 * the played champion's own seat and its role opponent. Only then has the draft
 * refined the answer (#2361): the role opponent alone also scores in the
 * similarity search, so `maxPossibleScore > 0` cannot tell the two apart.
 */
export function refinesBeyondMatchup(request: CompositionBuildRequest | null | undefined): boolean {
  if (!request) return false
  return request.allies.some(slot => slot.position !== request.position)
    || request.enemies.some(slot => slot.position !== request.position)
}
