import type { RankHistoryEntry } from '#shared/types/rank-history'
import type { PlayerGame } from '~/types/record'
import { lpDeltaOf } from '~/utils/lp-history'

/**
 * The player's session so far: the games they started since local midnight,
 * their record, the time they spent in them and what the day's Solo/Duo games
 * did to their LP. Read off the same games and LP snapshots as the rest of the
 * dashboard — nothing estimated: the LP is the sum of the gains the local
 * history could measure (`lpDeltaOf`), and says how many of the day's Solo/Duo
 * games that covers.
 */

const SOLO_QUEUE = 420

export interface DaySummary {
  games: number
  wins: number
  losses: number
  /** Time spent in the day's games, in seconds. */
  seconds: number
  /** Net LP over the day's measured Solo/Duo games; null when none is measured. */
  lp: number | null
  /** Solo/Duo games of the day whose LP was measured, out of `soloGames`. */
  lpGames: number
  soloGames: number
}

/** Local midnight of `now`'s day, in epoch milliseconds. */
export function startOfDay(now: Date): number {
  const midnight = new Date(now)
  midnight.setHours(0, 0, 0, 0)
  return midnight.getTime()
}

/**
 * The summary of `games` started since local midnight, or null when none was.
 * A game counts on the day it started; remakes are not counted. `allGames` and
 * `history` are what each game's LP is measured against.
 */
export function todayOf(
  games: PlayerGame[],
  allGames: PlayerGame[],
  history: RankHistoryEntry[],
  now = new Date(),
): DaySummary | null {
  const since = startOfDay(now)
  const today = games.filter(game => !game.remake && game.playedAt >= since)
  if (today.length === 0) return null

  const wins = today.filter(game => game.win).length
  const solo = today.filter(game => game.queueId === SOLO_QUEUE)
  const deltas = solo
    .map(game => lpDeltaOf(game, allGames, history))
    .filter((delta): delta is number => delta !== null)

  return {
    games: today.length,
    wins,
    losses: today.length - wins,
    seconds: today.reduce((sum, game) => sum + game.durationSeconds, 0),
    lp: deltas.length ? deltas.reduce((sum, delta) => sum + delta, 0) : null,
    lpGames: deltas.length,
    soloGames: solo.length,
  }
}

/** The qualifier on a partial LP sum — some of the day's Solo/Duo games unmeasured — or null. */
export function partialLpNote(summary: DaySummary | null): string | null {
  if (!summary || summary.lp === null || summary.lpGames === summary.soloGames) return null
  return `Measured on ${summary.lpGames} of today's ${summary.soloGames} Solo/Duo games.`
}

/** A time played as `1h 52m`, `38m`, or `<1m`. */
export function formatPlayTime(seconds: number): string {
  const minutes = Math.floor(seconds / 60)
  if (minutes < 1) return '<1m'
  const hours = Math.floor(minutes / 60)
  return hours ? `${hours}h ${`${minutes % 60}`.padStart(2, '0')}m` : `${minutes}m`
}
