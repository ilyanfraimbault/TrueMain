import type { LoadingPlayer } from '~/types/loading'
import { LANES } from '~/types/draft'
import type { LaneEdge, MatchupRecord } from '~/utils/lane-edge'
import { laneEdge } from '~/utils/lane-edge'

interface MatchupsAnswer {
  matchups: { opponentChampionId: number, games: number, wins: number }[]
}

/**
 * Each lane's edge (#1863), keyed by position, from the loading screen's
 * players: ours against theirs at the same position. The head-to-head of the
 * two champions is asked of TrueMain once per pair and kept for the session;
 * the players' forms fill in as the shell reads them, and the lane moves with
 * them. A lane missing either player — a queue without roles, a roster still
 * being read — has no edge.
 */
export function useLaneEdges(lines: MaybeRefOrGetter<LoadingPlayer[]>) {
  /** By `ally-enemy-position`; null once asked and not answered. */
  const matchups = useState<Record<string, MatchupRecord | null>>('lane-matchups', () => ({}))

  const pairs = computed(() => {
    const players = toValue(lines)
    const ours = players.find(player => player.isMe)?.team ?? 'ORDER'
    return LANES.flatMap((position) => {
      const ally = players.find(player => player.team === ours && player.position === position)
      const enemy = players.find(player => player.team !== ours && player.position === position)
      return ally && enemy ? [{ position, ally, enemy, key: `${ally.championId}-${enemy.championId}-${position}` }] : []
    })
  })

  watch(pairs, (current) => {
    for (const { position, ally, enemy, key } of current) {
      if (key in matchups.value) continue
      matchups.value = { ...matchups.value, [key]: null }
      apiGet<MatchupsAnswer>(`/champions/${ally.championId}/matchups`, { position, opponent: enemy.championId }, { background: true })
        .then((answer) => {
          const entry = answer.matchups.find(matchup => matchup.opponentChampionId === enemy.championId)
          if (entry) matchups.value = { ...matchups.value, [key]: { games: entry.games, wins: entry.wins } }
        })
        .catch(() => {})
    }
  }, { immediate: true })

  return computed(() => new Map<string, LaneEdge>(
    pairs.value.map(({ position, ally, enemy, key }) =>
      [position, laneEdge(position, matchups.value[key], ally.laning, enemy.laning)]),
  ))
}
