import type { ChampionMatchups } from '~~/shared/types/champions'
import type { ChampionPosition } from '~/utils/positions'
import { fetchErrorStatus } from '~/utils/errors'

export interface UseChampionMatchupsOptions {
  /**
   * When set, scope to a single player via
   * `GET /api/truemains/{nameTag}/champions/{id}/matchups` instead of the
   * global endpoint. Same contract, different pool.
   */
  nameTag?: MaybeRefOrGetter<string | undefined>
  /**
   * When set, ask the backend for just this opponent's head-to-head (a single
   * entry, or none) rather than the full leaderboard. Sent as `?opponent=<id>`.
   */
  opponentChampionId?: MaybeRefOrGetter<number | null | undefined>
  /**
   * Elo filter (exact tier or cumulative "X+" threshold) for the global slice.
   * Ignored for the player-scoped (`nameTag`) route — a single player's games
   * are one rank. Sent as `?eloBracket=<value>`.
   */
  eloBracket?: MaybeRefOrGetter<string | null | undefined>
}

/**
 * Fetches a champion's lane matchups at a position: the full list the table
 * slices into best/worst, or — when `opponentChampionId` is set — just that one
 * head-to-head (the backend narrows the self-join and drops the games floor to
 * one). Global by default; pass `nameTag` to scope to one player. Fires once a
 * position is known. A 404 (unknown player) resolves to null so the table can
 * render an empty state rather than an error.
 */
export function useChampionMatchups(
  championId: MaybeRefOrGetter<number>,
  position: MaybeRefOrGetter<ChampionPosition | null>,
  options: UseChampionMatchupsOptions = {},
) {
  const championIdRef = computed(() => toValue(championId))
  const positionRef = computed(() => toValue(position))
  const nameTagRef = computed(() => {
    const value = toValue(options.nameTag)
    return value && value.length > 0 ? value : undefined
  })
  const opponentRef = computed(() => {
    const value = toValue(options.opponentChampionId)
    return value == null ? undefined : value
  })
  const eloBracketRef = computed(() => toValue(options.eloBracket) || undefined)

  return useLazyAsyncData<ChampionMatchups | null>(
    () => [
      'champion-matchups',
      nameTagRef.value ?? 'global',
      championIdRef.value,
      positionRef.value ?? '',
      opponentRef.value ?? '',
      // Only the global slice varies by bracket, but keying it always keeps the
      // cache entry distinct when the filter changes.
      nameTagRef.value ? '' : eloBracketRef.value ?? '',
    ].join('-'),
    async () => {
      const position = positionRef.value
      if (!position) return null

      const query: Record<string, string> = { position }
      if (opponentRef.value != null) query.opponent = String(opponentRef.value)

      const nameTag = nameTagRef.value
      // The player-scoped route is one player's own games — a rank filter is
      // meaningless there, so only the global slice forwards the bracket.
      if (!nameTag && eloBracketRef.value) query.eloBracket = eloBracketRef.value

      const path = nameTag
        ? `/api/truemains/${encodeURIComponent(nameTag)}/champions/${championIdRef.value}/matchups`
        : `/api/champions/${championIdRef.value}/matchups`

      try {
        return await $fetch<ChampionMatchups>(path, { query })
      }
      catch (error: unknown) {
        // Unknown player (player-scoped route) → empty state, not an error.
        // Any other status propagates so callers can surface it.
        if (fetchErrorStatus(error) === 404) return null
        throw error
      }
    },
    {
      watch: [championIdRef, positionRef, nameTagRef, opponentRef, eloBracketRef],
      server: false,
    },
  )
}
