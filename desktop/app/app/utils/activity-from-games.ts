import type { ActivityBucket, ActivityMode, ActivitySeries, TruemainActivityResponse } from '#shared/types/activity'
import type { PlayerGame } from '~/types/record'

const DAY_MS = 86_400_000

const startOfUtcDay = (ms: number) => ms - (ms % DAY_MS)
const isoDay = (ms: number) => new Date(ms).toISOString().slice(0, 10)

function series(mode: ActivityMode, buckets: ActivityBucket[]): ActivitySeries {
  const games = buckets.reduce((sum, bucket) => sum + bucket.games, 0)
  const wins = buckets.reduce((sum, bucket) => sum + bucket.wins, 0)
  return {
    mode,
    patch: null,
    coverageFromUtc: buckets[0]?.startUtc ?? null,
    coverageToUtc: buckets.at(-1)?.startUtc ?? null,
    buckets,
    games,
    wins,
    winRate: games ? wins / games : null,
  }
}

/** The last `days` UTC days, today included, one cell each — played or not. */
function calendar(mode: ActivityMode, games: PlayerGame[], days: number, now: number): ActivitySeries {
  const today = startOfUtcDay(now)
  const buckets: ActivityBucket[] = Array.from({ length: days }, (_, index) => {
    const start = today - (days - 1 - index) * DAY_MS
    const played = games.filter(game => startOfUtcDay(game.playedAt) === start)
    const wins = played.filter(game => game.win).length
    return {
      key: isoDay(start),
      startUtc: new Date(start).toISOString(),
      games: played.length,
      wins,
      winRate: played.length ? wins / played.length : null,
      championId: null,
    }
  })
  return series(mode, buckets)
}

/**
 * The activity grid of the player in the client (the profile's
 * `ProfileActivityHeatmap`), folded from the games the client returned rather
 * than read from TrueMain: the dashboard's player need not be a true main, and
 * TrueMain holds only the games it ingested. Same shape as `/activity`, on the
 * same UTC days. The client's games carry no patch, so the patch window is left
 * empty and the dashboard does not offer it. Remakes count for nothing, as
 * everywhere else.
 */
export function activityFromGames(all: PlayerGame[], now: number = Date.now()): TruemainActivityResponse {
  const games = all.filter(game => !game.remake)
  const today = startOfUtcDay(now)
  const todays = games
    .filter(game => startOfUtcDay(game.playedAt) === today)
    .sort((a, b) => a.playedAt - b.playedAt)
    .map(game => ({
      key: String(game.gameId),
      startUtc: new Date(game.playedAt).toISOString(),
      games: 1,
      wins: game.win ? 1 : 0,
      winRate: game.win ? 1 : 0,
      championId: game.championId,
    }))
  return {
    day: series('day', todays),
    week: calendar('week', games, 7, now),
    month: calendar('month', games, 30, now),
    patch: series('patch', []),
  }
}
