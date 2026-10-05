import type { PlayerGame } from '~/types/record'
import type { MetricDefinition, QueueFilter } from '~/utils/player-form'
import { MIN_GAMES_FOR_DELTA, METRICS, QUEUE_FILTERS, counted, gamesIn, readMetric } from '~/utils/player-form'
import { LANE_LABELS, type Lane } from '~/types/draft'

/**
 * Measurable goals: a target the player sets on one of the dashboard's metrics
 * over their next games — "CS / min ≥ 7 over the next 5 Solo games". They are
 * measured from the games the player's own client lists, kept on this machine
 * and never sent anywhere (#1913). Each counted game is frozen into the goal,
 * so a goal stays decided once its games leave the client's bounded history.
 */

export type GoalComparator = 'atLeast' | 'atMost'
/** `average`: the metric over the N games meets the threshold. `eachGame`: K of the N games do. */
export type GoalMode = 'average' | 'eachGame'
export type GoalStatus = 'active' | 'done' | 'missed' | 'abandoned'

export interface GoalScope {
  queue: QueueFilter
  championId?: number | null
  position?: string | null
}

/** One counted game, frozen: enough to recompute a ratio-of-sums average without the game. */
export interface GoalResult {
  gameId: number
  playedAt: number
  value: number
  numerator: number
  denominator: number
}

export interface Goal {
  id: string
  riotId: string
  /** A `METRICS` key. */
  metric: string
  comparator: GoalComparator
  /** In the metric's own unit: a share (0..1) for the percentages. */
  threshold: number
  /** N, the games the goal runs over. */
  games: number
  mode: GoalMode
  /** K, for `eachGame`: how many of the N games must meet the threshold. */
  hitsNeeded: number
  scope: GoalScope
  /** Epoch ms: only games started from then on count. */
  createdAt: number
  status: GoalStatus
  results: GoalResult[]
}

export const MAX_ACTIVE_GOALS = 3
export const MAX_GOAL_GAMES = 20
export const DEFAULT_GOAL_GAMES = 5

interface GoalSpec {
  /** The metric's displayed precision: the verdict reads the value the player sees. */
  step: number
  mode: GoalMode
  /** One game's share of the ratio of sums `METRICS[key].over` takes, or null where the game cannot say. */
  parts: (game: PlayerGame) => [number, number] | null
}

const minutes = (game: PlayerGame) => Math.max(game.durationSeconds, 1) / 60
const perMinute = (value: (game: PlayerGame) => number, step: number): GoalSpec =>
  ({ step, mode: 'average', parts: game => [value(game), minutes(game)] })

const SPECS: Record<string, GoalSpec> = {
  kda: { step: 0.01, mode: 'average', parts: game => [game.kills + game.assists, game.deaths] },
  kp: { step: 0.01, mode: 'eachGame', parts: game => (game.teamKills ? [game.kills + game.assists, game.teamKills] : null) },
  cs: perMinute(game => game.cs, 0.1),
  damage: perMinute(game => game.damageToChampions, 1),
  share: {
    step: 0.01,
    mode: 'average',
    parts: game => (game.teamDamageToChampions ? [game.damageToChampions, game.teamDamageToChampions] : null),
  },
  gold: perMinute(game => game.gold, 1),
  vision: perMinute(game => game.visionScore, 0.01),
  deaths: { step: 0.1, mode: 'eachGame', parts: game => [game.deaths, 1] },
}

export const metricOf = (key: string): MetricDefinition | undefined => METRICS.find(metric => metric.key === key)
export const isPercent = (key: string) => key === 'kp' || key === 'share'
export const stepOf = (key: string) => SPECS[key]?.step ?? 0.01
export const defaultModeOf = (key: string): GoalMode => SPECS[key]?.mode ?? 'average'
export const defaultComparatorOf = (key: string): GoalComparator => (metricOf(key)?.lowerIsBetter ? 'atMost' : 'atLeast')

/** A value at the metric's displayed precision. */
export function roundToStep(key: string, value: number): number {
  const step = stepOf(key)
  return Number((Math.round(value / step) * step).toFixed(4))
}

export function meets(goal: Pick<Goal, 'metric' | 'comparator' | 'threshold'>, value: number): boolean {
  const shown = roundToStep(goal.metric, value)
  return goal.comparator === 'atLeast' ? shown >= goal.threshold - 1e-9 : shown <= goal.threshold + 1e-9
}

export function inScope(game: PlayerGame, scope: GoalScope): boolean {
  if (gamesIn([game], scope.queue).length === 0) return false
  if (scope.championId && game.championId !== scope.championId) return false
  if (scope.position && game.position !== scope.position) return false
  return true
}

/** The ratio of sums over frozen results — what `METRICS[key].over` gives over the games themselves. */
export function averageOf(key: string, results: GoalResult[]): number | null {
  if (results.length === 0) return null
  const top = results.reduce((sum, result) => sum + result.numerator, 0)
  const bottom = results.reduce((sum, result) => sum + result.denominator, 0)
  // KDA floors deaths at one over the sample, as `METRICS.kda.over` does.
  if (key === 'kda') return top / Math.max(bottom, 1)
  return bottom > 0 ? top / bottom : null
}

export interface GoalEvaluation {
  /** The counted games, oldest first: the frozen ones, then any this read added. */
  results: GoalResult[]
  counted: number
  /** The average over the counted games, in the metric's unit. */
  value: number | null
  /** Counted games that met the threshold on their own. */
  hits: number
  status: GoalStatus
  /** The games read do not reach back to where the run left off, and the client has older ones. */
  needsOlder: boolean
}

/**
 * Where a goal stands against the games read. Games started from its creation,
 * in scope, not remakes and measurable for its metric are frozen into it, oldest
 * first, until N are counted; a game the metric cannot measure extends the run.
 * It is decided as soon as the outcome is settled. `hasOlder` says the client
 * has games before the oldest one read: while the read does not reach back to
 * where the run left off, nothing is added, or a later game would be counted
 * before an earlier one.
 */
export function evaluate(goal: Goal, newestFirst: PlayerGame[], hasOlder = false): GoalEvaluation {
  const spec = SPECS[goal.metric]
  const metric = metricOf(goal.metric)
  const results = [...goal.results]
  let needsOlder = false

  if (goal.status === 'active' && spec && metric && results.length < goal.games) {
    const since = results.at(-1)?.playedAt ?? goal.createdAt
    const oldestRead = newestFirst.at(-1)?.playedAt
    needsOlder = hasOlder && (oldestRead === undefined || oldestRead > since)
    if (!needsOlder) {
      const taken = new Set(results.map(result => result.gameId))
      const fresh = newestFirst
        .filter(game => game.playedAt >= goal.createdAt && game.playedAt > (results.at(-1)?.playedAt ?? -Infinity))
        .filter(game => !game.remake && !taken.has(game.gameId) && inScope(game, goal.scope))
        .sort((a, b) => a.playedAt - b.playedAt)
      for (const game of fresh) {
        if (results.length >= goal.games || settled(goal, results)) break
        const value = metric.perGame(game)
        const parts = spec.parts(game)
        if (value === null || parts === null) continue
        results.push({ gameId: game.gameId, playedAt: game.playedAt, value, numerator: parts[0], denominator: parts[1] })
      }
    }
  }

  const hits = results.filter(result => meets(goal, result.value)).length
  const value = averageOf(goal.metric, results)
  const status = goal.status === 'active' ? settled(goal, results) ?? 'active' : goal.status
  return { results, counted: results.length, value, hits, status, needsOlder }
}

/** `done` or `missed` once nothing the remaining games do can change it; null while open. */
function settled(goal: Goal, results: GoalResult[]): 'done' | 'missed' | null {
  if (goal.mode === 'eachGame') {
    const hits = results.filter(result => meets(goal, result.value)).length
    if (hits >= goal.hitsNeeded) return 'done'
    if (results.length - hits > goal.games - goal.hitsNeeded) return 'missed'
    return null
  }
  if (results.length < goal.games) return null
  const value = averageOf(goal.metric, results)
  return value !== null && meets(goal, value) ? 'done' : 'missed'
}

/** A threshold as the player reads it: a percentage, or the metric's format (whole deaths). */
export function formatThreshold(key: string, threshold: number): string {
  if (isPercent(key)) return `${Math.round(threshold * 100)}%`
  if (key === 'deaths' && Number.isInteger(threshold)) return String(threshold)
  return metricOf(key)?.format(threshold) ?? String(threshold)
}

const QUEUE_LABEL: Record<QueueFilter, string> = Object.fromEntries(QUEUE_FILTERS.map(option => [option.value, option.label])) as Record<QueueFilter, string>

/** The goal as a plain sentence: "Deaths ≤ 5 in each of the next 5 Solo games". */
export function goalSentence(goal: GoalDraft, championName?: string): string {
  const metric = metricOf(goal.metric)
  const target = `${metric?.label ?? goal.metric} ${goal.comparator === 'atLeast' ? '≥' : '≤'} ${formatThreshold(goal.metric, goal.threshold)}`
  const queue = goal.scope.queue === 'all' ? '' : `${QUEUE_LABEL[goal.scope.queue]} `
  const where = [
    championName,
    goal.scope.position ? LANE_LABELS[goal.scope.position as Lane] ?? goal.scope.position : null,
  ].filter(Boolean).join(' · ')
  const plural = goal.games === 1 ? 'game' : 'games'
  const run = goal.mode === 'average'
    ? `over the next ${goal.games} ${queue}${plural}`
    : goal.hitsNeeded === goal.games
      ? goal.games === 1 ? `in the next ${queue}game` : `in each of the next ${goal.games} ${queue}games`
      : `in ${goal.hitsNeeded} of the next ${goal.games} ${queue}games`
  return where ? `${target} ${run} · ${where}` : `${target} ${run}`
}

export type GoalDraft = Omit<Goal, 'id' | 'riotId' | 'createdAt' | 'status' | 'results'>

/** A new goal on the metric, with its defaults and the given threshold. */
export function draftGoal(metric: string, threshold: number, queue: QueueFilter = 'solo'): GoalDraft {
  return {
    metric,
    comparator: defaultComparatorOf(metric),
    threshold: roundToStep(metric, threshold),
    games: DEFAULT_GOAL_GAMES,
    mode: defaultModeOf(metric),
    hitsNeeded: DEFAULT_GOAL_GAMES,
    scope: { queue },
  }
}

/** Where suggestions are measured: the dashboard's queue when it is one queue of the Rift, Solo otherwise. */
export const suggestionQueue = (filter: QueueFilter): QueueFilter =>
  filter === 'solo' || filter === 'flex' || filter === 'normal' ? filter : 'solo'

/** The share of the counted games played as support above which a player reads as one. */
const SUPPORT_SHARE = 0.5

/**
 * Up to three goals on the metrics where the player's recent form is furthest
 * below their own average, each set at that average: a target taken from their
 * own games, never a benchmark TrueMain does not have. None below the sample
 * the dashboard's deltas need, none on a metric an active goal already tracks,
 * no CS for a player who mostly supports — and vision first for one who does.
 */
export function suggestGoals(newestFirst: PlayerGame[], active: Goal[], filter: QueueFilter = 'solo'): GoalDraft[] {
  const queue = suggestionQueue(filter)
  const games = counted(gamesIn(newestFirst, queue))
  if (games.length < MIN_GAMES_FOR_DELTA) return []
  const support = games.filter(game => game.position === 'UTILITY').length / games.length > SUPPORT_SHARE
  const taken = new Set(active.map(goal => goal.metric))

  const ranked = METRICS
    .filter(metric => !taken.has(metric.key) && !(support && metric.key === 'cs'))
    .map((metric) => {
      const reading = readMetric(metric, games)
      const average = metric.over(games)
      if (reading.improved !== false || reading.delta === null || !average) return null
      return { metric, average, gap: Math.abs(reading.delta / average) }
    })
    .filter(entry => entry !== null)
    .sort((a, b) => (support ? Number(b.metric.key === 'vision') - Number(a.metric.key === 'vision') : 0) || b.gap - a.gap)

  return ranked.slice(0, Math.max(MAX_ACTIVE_GOALS - active.length, 0)).map(entry => draftGoal(entry.metric.key, entry.average, queue))
}
