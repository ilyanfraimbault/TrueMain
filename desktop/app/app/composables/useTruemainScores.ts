import type { MatchSummariesResponse } from '#shared/types/matches'
import type { GameScore } from '~/utils/match-summary'

/** TrueMain's page size ceiling for a player's matches. */
const MATCHES_PAGE = 50

/**
 * TrueMain's own score of the player's games it holds, keyed by the client's
 * game id (#2075): a match row prefers it to grading the client's copy, so a
 * game reads the same on the dashboard as on the site. One read of the latest
 * page per Riot ID and session; a player TrueMain does not track (a 404), and
 * every game past that page, is graded from the client — the same model, pinned
 * by the shared vectors.
 */
export function useTruemainScores(nameTag: MaybeRefOrGetter<string>) {
  const { record } = usePlayerRecord()
  const scores = useState<Record<string, Record<string, GameScore>>>('truemain-scores', () => ({}))

  watch(() => [toValue(nameTag), record.value?.platformId ?? null] as const, async ([tag, platformId]) => {
    if (!tag || !platformId || scores.value[tag]) return
    scores.value = { ...scores.value, [tag]: {} }
    try {
      const answer = await apiGet<MatchSummariesResponse>(
        `/truemains/${encodeURIComponent(tag)}/matches`,
        { pageSize: MATCHES_PAGE },
        { background: true },
      )
      const prefix = `${platformId}_`
      const byGame: Record<string, GameScore> = {}
      for (const { matchId, self } of answer.matches) {
        if (!matchId.startsWith(prefix)) continue
        byGame[matchId.slice(prefix.length)] = {
          performanceScore: self.performanceScore,
          placement: self.placement,
          isMvp: self.isMvp,
          isAce: self.isAce,
        }
      }
      scores.value = { ...scores.value, [tag]: byGame }
    }
    catch {
      // Not tracked, or TrueMain unreachable: the client grades every game.
    }
  }, { immediate: true })

  return computed(() => scores.value[toValue(nameTag)] ?? {})
}
