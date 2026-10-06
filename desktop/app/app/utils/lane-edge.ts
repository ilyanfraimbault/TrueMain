import type { LaningForm } from '~/types/loading'

/**
 * Which side a lane favours, and by how much (#1863). It starts from the two
 * champions' head-to-head at that position on TrueMain — pulled towards an
 * even lane while the sample is thin — and moves by each player's form on the
 * champion they are on: their win rate over their last games on it, and their
 * gold, creep score and experience leads over their lane opponent at fifteen
 * minutes, weighed per role. An estimate for the lane, not a score of the
 * player: the figures behind it are not shown.
 */

export type LaneMetric = 'winRate' | 'gold' | 'cs' | 'xp'

/**
 * What each figure weighs in a role's form. A jungler's farm says little next
 * to their gold and experience; a support's gold and farm say almost nothing,
 * so their win rate carries most of it.
 */
export const ROLE_WEIGHTS: Record<string, Record<LaneMetric, number>> = {
  TOP: { winRate: 0.4, gold: 0.25, cs: 0.2, xp: 0.15 },
  JUNGLE: { winRate: 0.5, gold: 0.3, cs: 0, xp: 0.2 },
  MIDDLE: { winRate: 0.4, gold: 0.25, cs: 0.2, xp: 0.15 },
  BOTTOM: { winRate: 0.4, gold: 0.25, cs: 0.25, xp: 0.1 },
  UTILITY: { winRate: 0.7, gold: 0.15, cs: 0, xp: 0.15 },
}

/** A lead this large at fifteen minutes counts in full; past it, no more. */
const FULL_LEAD = { gold: 1000, cs: 15, xp: 800 }
/** Games of an even record a player's win rate starts from, so 1 game out of 1 is not 100 %. */
const WIN_RATE_PRIOR = 4
/** Games the gaps are pulled towards zero by, for the same reason. */
const GAPS_PRIOR = 2
/** Head-to-head games of an even matchup the matchup's win rate starts from. */
const MATCHUP_PRIOR = 30
/** How far one player's full form moves the lane, in log-odds: both at their extremes move it ~27 points. */
const FORM_LOG_ODDS = 0.6
/** A lane this close to even points at neither side. */
const EVEN_MARGIN = 0.02

const clamp = (value: number) => Math.max(-1, Math.min(1, value))
const pulled = (value: number | null, samples: number, prior: number) =>
  value === null ? 0 : (value * samples) / (samples + prior)

/**
 * A player's form on their champion, from -1 to 1, weighed for `position`.
 * Zero — an even player — when nothing was read: an anonymous player, a
 * history the client did not serve, a first time on the champion.
 */
export function laneForm(laning: LaningForm | null | undefined, position: string): number {
  const weights = ROLE_WEIGHTS[position]
  if (!laning || !weights || laning.games === 0) return 0
  const winRate = (laning.wins + WIN_RATE_PRIOR / 2) / (laning.games + WIN_RATE_PRIOR)
  const lead = (value: number | null, full: number) => clamp(pulled(value, laning.measured, GAPS_PRIOR) / full)
  return clamp(
    weights.winRate * clamp((winRate - 0.5) / 0.25)
    + weights.gold * lead(laning.goldDiff15, FULL_LEAD.gold)
    + weights.cs * lead(laning.csDiff15, FULL_LEAD.cs)
    + weights.xp * lead(laning.xpDiff15, FULL_LEAD.xp),
  )
}

/** The head-to-head the ally's champion holds at the position, as TrueMain read it. */
export interface MatchupRecord {
  games: number
  wins: number
}

/** The ally champion's chance in the head-to-head, pulled towards even on a thin sample; even with none. */
export function matchupChance(record: MatchupRecord | null | undefined): number {
  if (!record || record.games <= 0) return 0.5
  return (record.wins + MATCHUP_PRIOR / 2) / (record.games + MATCHUP_PRIOR)
}

export type LaneSide = 'ally' | 'enemy' | 'even'

export interface LaneEdge {
  /** The ally side's chance in the lane, 0..1. */
  chance: number
  side: LaneSide
  /** The favoured side's chance, whole percent — what the arrow carries. */
  percent: number
  /** The head-to-head it started from, and how many games it held. */
  matchup: number
  matchupGames: number
}

const logit = (p: number) => Math.log(p / (1 - p))
const sigmoid = (x: number) => 1 / (1 + Math.exp(-x))

export function laneEdge(
  position: string,
  record: MatchupRecord | null | undefined,
  ally: LaningForm | null | undefined,
  enemy: LaningForm | null | undefined,
): LaneEdge {
  const matchup = matchupChance(record)
  const shift = FORM_LOG_ODDS * (laneForm(ally, position) - laneForm(enemy, position))
  const chance = sigmoid(logit(matchup) + shift)
  const side: LaneSide = chance >= 0.5 + EVEN_MARGIN ? 'ally' : chance <= 0.5 - EVEN_MARGIN ? 'enemy' : 'even'
  return {
    chance,
    side,
    percent: Math.round((side === 'enemy' ? 1 - chance : chance) * 100),
    matchup,
    matchupGames: record?.games ?? 0,
  }
}
