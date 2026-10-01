import type { PlayerGame } from '~/types/record'

/**
 * The dashboard's reading of the player's recent games: form against their
 * own average, per champion and per lane. Every figure is a sum or a ratio
 * over games the client listed — nothing here scores the player as a whole
 * (#1683: no composite player rating).
 */

export type QueueFilter = 'all' | 'solo' | 'flex' | 'normal' | 'aram'

export const QUEUE_FILTERS: { value: QueueFilter, label: string, queues: number[] | null }[] = [
  { value: 'all', label: 'All', queues: null },
  { value: 'solo', label: 'Solo', queues: [420] },
  { value: 'flex', label: 'Flex', queues: [440] },
  { value: 'normal', label: 'Normal', queues: [400, 430, 480, 490] },
  { value: 'aram', label: 'ARAM', queues: [450] },
]

export function gamesIn(games: PlayerGame[], filter: QueueFilter): PlayerGame[] {
  const queues = QUEUE_FILTERS.find(entry => entry.value === filter)?.queues
  return queues ? games.filter(game => queues.includes(game.queueId)) : games
}

/** The games that count: remakes are listed but measure nothing. */
export const counted = (games: PlayerGame[]) => games.filter(game => !game.remake)

/** How many of the latest games make "recent form". */
export const RECENT_GAMES = 5

/** Below this many counted games a recent-vs-average delta compares a sample with itself. */
const MIN_GAMES_FOR_DELTA = RECENT_GAMES + 3

const minutes = (game: PlayerGame) => Math.max(game.durationSeconds, 1) / 60

/** `Σ numerator / Σ denominator` over the games that have both, or null. */
function ratioOfSums(
  games: PlayerGame[],
  numerator: (game: PlayerGame) => number | null,
  denominator: (game: PlayerGame) => number | null,
): number | null {
  let top = 0
  let bottom = 0
  for (const game of games) {
    const n = numerator(game)
    const d = denominator(game)
    if (n === null || d === null) continue
    top += n
    bottom += d
  }
  return bottom > 0 ? top / bottom : null
}

export interface MetricDefinition {
  key: string
  label: string
  /** One game's value, for the sparkline; null where the game cannot say. */
  perGame: (game: PlayerGame) => number | null
  /** The value over several games — a ratio of sums, so a short game weighs what it lasted. */
  over: (games: PlayerGame[]) => number | null
  format: (value: number) => string
  /** Deaths: fewer is the better direction. */
  lowerIsBetter?: boolean
}

const perMinute = (value: (game: PlayerGame) => number) => ({
  perGame: (game: PlayerGame) => value(game) / minutes(game),
  over: (games: PlayerGame[]) => ratioOfSums(games, value, minutes),
})

const fixed = (digits: number) => (value: number) => value.toFixed(digits)
const percent = (value: number) => `${Math.round(value * 100)}%`

export const METRICS: MetricDefinition[] = [
  {
    key: 'kda',
    label: 'KDA',
    perGame: game => (game.kills + game.assists) / Math.max(game.deaths, 1),
    // Deaths floored at one over the sample, as per game: a deathless stretch is a big number, not none.
    over: games => (games.length ? kdaOf(sumLine(games)) : null),
    format: fixed(2),
  },
  {
    key: 'kp',
    label: 'Kill part.',
    perGame: game => (game.teamKills ? (game.kills + game.assists) / game.teamKills : null),
    over: games => ratioOfSums(games, game => (game.teamKills ? game.kills + game.assists : null), game => game.teamKills || null),
    format: percent,
  },
  { key: 'cs', label: 'CS / min', ...perMinute(game => game.cs), format: fixed(1) },
  { key: 'damage', label: 'DMG / min', ...perMinute(game => game.damageToChampions), format: fixed(0) },
  {
    key: 'share',
    label: 'Damage share',
    perGame: game => (game.teamDamageToChampions ? game.damageToChampions / game.teamDamageToChampions : null),
    over: games => ratioOfSums(
      games,
      game => (game.teamDamageToChampions ? game.damageToChampions : null),
      game => game.teamDamageToChampions || null,
    ),
    format: percent,
  },
  { key: 'gold', label: 'Gold / min', ...perMinute(game => game.gold), format: fixed(0) },
  { key: 'vision', label: 'Vision / min', ...perMinute(game => game.visionScore), format: fixed(2) },
  {
    key: 'deaths',
    label: 'Deaths',
    perGame: game => game.deaths,
    over: games => (games.length ? games.reduce((sum, game) => sum + game.deaths, 0) / games.length : null),
    format: fixed(1),
    lowerIsBetter: true,
  },
]

export interface MetricReading {
  metric: MetricDefinition
  /** Over the latest `RECENT_GAMES`, or over the whole sample when it is too short to split. */
  value: number | null
  /** `value` against the whole sample; null when the sample is too short to compare. */
  delta: number | null
  /** Whether the delta moved the way that is better for this metric; null for a flat one. */
  improved: boolean | null
  /** Oldest first, for the sparkline. */
  series: number[]
}

/** A delta smaller than this share of the average is noise, drawn flat. */
const FLAT_SHARE = 0.03

/** One tile: the player's recent form on a metric, read against their own average. */
export function readMetric(metric: MetricDefinition, newestFirst: PlayerGame[]): MetricReading {
  const games = counted(newestFirst)
  const series = games
    .map(metric.perGame)
    .filter((value): value is number => value !== null)
    .reverse()
  const average = metric.over(games)

  if (games.length < MIN_GAMES_FOR_DELTA || average === null) {
    return { metric, value: average, delta: null, improved: null, series }
  }

  const recent = metric.over(games.slice(0, RECENT_GAMES))
  if (recent === null) return { metric, value: average, delta: null, improved: null, series }

  const delta = recent - average
  const flat = Math.abs(delta) <= Math.abs(average) * FLAT_SHARE
  const improved = flat ? null : (delta > 0) !== Boolean(metric.lowerIsBetter)
  return { metric, value: recent, delta, improved, series }
}

export interface ChampionLine {
  championId: number
  /** The lane it was played on most, for its build page; null if only off the Rift. */
  position: string | null
  games: number
  wins: number
  kills: number
  deaths: number
  assists: number
}

/** The player's champions over the sample, most played first. */
export function championLines(newestFirst: PlayerGame[]): ChampionLine[] {
  const byChampion = new Map<number, PlayerGame[]>()
  for (const game of counted(newestFirst)) {
    byChampion.set(game.championId, [...(byChampion.get(game.championId) ?? []), game])
  }
  const lines = [...byChampion.entries()].map(([championId, games]) => ({
    championId,
    position: mostCommon(games.map(game => game.position)),
    games: games.length,
    wins: games.filter(game => game.win).length,
    ...sumLine(games),
  }))
  return lines.sort((a, b) => b.games - a.games || b.wins - a.wins || a.championId - b.championId)
}

function mostCommon(values: (string | null)[]): string | null {
  const counts = new Map<string, number>()
  for (const value of values) if (value) counts.set(value, (counts.get(value) ?? 0) + 1)
  return [...counts.entries()].sort((a, b) => b[1] - a[1])[0]?.[0] ?? null
}

export function kdaOf(line: { kills: number, deaths: number, assists: number }) {
  return (line.kills + line.assists) / Math.max(line.deaths, 1)
}

function sumLine(games: PlayerGame[]) {
  return games.reduce(
    (sum, game) => ({ kills: sum.kills + game.kills, deaths: sum.deaths + game.deaths, assists: sum.assists + game.assists }),
    { kills: 0, deaths: 0, assists: 0 },
  )
}

export interface LaneLine {
  position: string
  games: number
  wins: number
}

const LANE_ORDER = ['TOP', 'JUNGLE', 'MIDDLE', 'BOTTOM', 'UTILITY']

/** Games and wins on each of the five lanes, in lane order — lanes never played included, at zero. */
export function laneLines(newestFirst: PlayerGame[]): LaneLine[] {
  const games = counted(newestFirst)
  return LANE_ORDER.map((position) => {
    const played = games.filter(game => game.position === position)
    return { position, games: played.length, wins: played.filter(game => game.win).length }
  })
}

/** A game's kill participation, 0..1, or null without its scoreboard. */
export const killParticipation = (game: PlayerGame) =>
  game.teamKills ? Math.min((game.kills + game.assists) / game.teamKills, 1) : null
