import type {
  MatchWinProbability,
  WinProbabilityObjective,
  WinProbabilitySwing,
} from '#shared/types/win-probability'
import type { MatchDetailResponse } from '#shared/types/match-detail'
import { buildWinProbability } from './win-probability-timeline'

/**
 * How the post-game curve's turning points read (#1911), shared by the site's
 * match detail, the desktop dashboard and the recording recap. Everything the
 * curve stores reads for team 100; these turn it to the viewer's side.
 */

/** How many turning points are listed. */
export const SWINGS_SHOWN = 5

/**
 * The detail's curve: TrueMain's own (computed at ingest) when it has one,
 * else the one the desktop builds from the client's timeline; null for a game
 * that gets none.
 */
export function resolveWinProbability(detail: Pick<MatchDetailResponse, 'winProbability' | 'winProbabilityTimeline'>): MatchWinProbability | null {
  if (detail.winProbability) return detail.winProbability
  return detail.winProbabilityTimeline ? buildWinProbability(detail.winProbabilityTimeline) : null
}

/** A team-100 chance or delta, read for `teamId`. */
export function forTeam(value: number, teamId: number, kind: 'probability' | 'delta'): number {
  if (teamId === 100) return value
  return kind === 'probability' ? 1 - value : -value
}

/** A delta as signed points ("+7", "−4", "+0.6" under one point). */
export function formatSwingPoints(delta: number): string {
  const points = delta * 100
  const magnitude = Math.abs(points)
  const text = magnitude < 0.95 ? magnitude.toFixed(1) : String(Math.round(magnitude))
  return `${points < 0 ? '−' : '+'}${text}`
}

/** Game time as `m:ss`. */
export function formatGameClock(ms: number): string {
  const seconds = Math.max(0, Math.floor(ms / 1000))
  return `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, '0')}`
}

const LANES: Record<string, string> = { TOP_LANE: 'top', MID_LANE: 'mid', BOT_LANE: 'bot' }
const TOWERS: Record<string, string> = {
  OUTER_TURRET: 'outer turret',
  INNER_TURRET: 'inner turret',
  BASE_TURRET: 'inhibitor turret',
  NEXUS_TURRET: 'Nexus turret',
}
const DRAKES: Record<string, string> = {
  AIR_DRAGON: 'Cloud Drake',
  CHEMTECH_DRAGON: 'Chemtech Drake',
  EARTH_DRAGON: 'Mountain Drake',
  MOUNTAIN_DRAGON: 'Mountain Drake',
  FIRE_DRAGON: 'Infernal Drake',
  HEXTECH_DRAGON: 'Hextech Drake',
  WATER_DRAGON: 'Ocean Drake',
  ELDER_DRAGON: 'Elder Drake',
}
const MONSTERS: Record<string, string> = {
  BARON_NASHOR: 'Baron',
  RIFTHERALD: 'Rift Herald',
  HORDE: 'Voidgrubs',
  ATAKHAN: 'Atakhan',
}

/** An epic monster's name, as a player says it. */
export function monsterLabel(monsterType: string, monsterSubType: string | null): string {
  if (monsterType === 'DRAGON') return (monsterSubType && DRAKES[monsterSubType]) || 'Drake'
  return MONSTERS[monsterType] ?? monsterType
}

export interface SwingContext {
  /** The side the viewer reads the game for. */
  perspectiveTeamId: number
  /** A participant's champion name. */
  championName: (participantId: number) => string
}

export interface DescribedSwing {
  /** What happened, from the viewer's side: "Your Ahri killed Zed (+2 assists)", "Enemy Baron". */
  label: string
  /** The event was for the viewer's side. */
  ours: boolean
  /** The change in the viewer's chance, −1..1. */
  delta: number
  /** Kills: the gold the kill gave, when the timeline carries it. */
  gold: number | null
}

/** A turning point in words, from the viewer's side. */
export function describeSwing(swing: WinProbabilitySwing, context: SwingContext): DescribedSwing {
  const ours = swing.teamId === context.perspectiveTeamId
  const side = ours ? 'Your' : 'Enemy'
  const lane = swing.lane ? LANES[swing.lane] ?? null : null
  let label: string
  switch (swing.kind) {
    case 'kill': {
      const assists = swing.assists > 0 ? ` (+${swing.assists} assist${swing.assists > 1 ? 's' : ''})` : ''
      label = `${side} ${context.championName(swing.killerId)} killed ${context.championName(swing.victimId)}${assists}`
      break
    }
    case 'turret': {
      const tower = (swing.towerType && TOWERS[swing.towerType]) || 'turret'
      label = `${ours ? 'Took' : 'Lost'} the ${tower}${lane ? `, ${lane}` : ''}`
      break
    }
    case 'inhibitor':
      label = `${ours ? 'Took' : 'Lost'} the ${lane ? `${lane} ` : ''}inhibitor`
      break
    case 'dragon':
      label = `${side} ${monsterLabel('DRAGON', swing.monsterSubType)}`
      break
    case 'elder':
      label = `${side} Elder Drake`
      break
    case 'baron':
      label = `${side} Baron`
      break
  }
  return {
    label,
    ours,
    delta: forTeam(swing.delta, context.perspectiveTeamId, 'delta'),
    gold: swing.kind === 'kill' ? swing.bounty : null,
  }
}

/** An epic monster in words, from the viewer's side: "Enemy Voidgrubs". */
export function describeObjective(objective: WinProbabilityObjective, perspectiveTeamId: number): string {
  const side = objective.teamId === perspectiveTeamId ? 'Your' : 'Enemy'
  return `${side} ${monsterLabel(objective.monsterType, objective.monsterSubType)}`
}
