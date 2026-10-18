import type {
  MatchWinProbability,
  WinProbabilityObjective,
  WinProbabilitySwing,
} from '#shared/types/win-probability'
import type { MatchDetailResponse } from '#shared/types/match-detail'
import { formatPointDelta } from '#shared/utils/deltas'
import { buildWinProbability } from './win-probability-timeline'
import { formatDuration } from './relativeTime'

/**
 * How the post-game curve's turning points read (#1911), shared by the desktop
 * dashboard's match detail and the recording recap. Everything the
 * curve stores reads for team 100; these turn it to the viewer's side.
 */

/** How many turning points are listed. */
export const SWINGS_SHOWN = 5

/**
 * The detail's curve, built from the client's timeline the desktop app attaches;
 * null on the site (the API sends none, #2009) and for a game that gets none.
 */
export function resolveWinProbability(detail: Pick<MatchDetailResponse, 'winProbabilityTimeline'>): MatchWinProbability | null {
  return detail.winProbabilityTimeline ? buildWinProbability(detail.winProbabilityTimeline) : null
}

/** A team-100 chance or delta, read for `teamId`. */
export function forTeam(value: number, teamId: number, kind: 'probability' | 'delta'): number {
  if (teamId === 100) return value
  return kind === 'probability' ? 1 - value : -value
}

/**
 * A swing as signed points, without the unit (the lists say it once): `+7`, `−4`,
 * and one decimal under a point (`+0.6`), where a whole number would round a
 * real if small swing to nothing.
 */
export function formatSwingPoints(delta: number): string {
  return formatPointDelta(delta, { digits: Math.abs(delta * 100) < 0.95 ? 1 : 0, unit: false })
}

/** Game time in milliseconds as `m:ss`. */
export function formatGameClock(ms: number): string {
  return formatDuration(ms / 1000)
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

/**
 * A turret or an inhibitor in words, from the viewer's side: "Took the outer
 * turret, mid", "Lost the bot inhibitor".
 */
export function structureLabel(
  kind: 'turret' | 'inhibitor',
  building: { ours: boolean, lane: string | null, towerType: string | null },
): string {
  const verb = building.ours ? 'Took' : 'Lost'
  const lane = building.lane ? LANES[building.lane] ?? null : null
  if (kind === 'inhibitor') {
    const inhibitor = lane ? lane + ' inhibitor' : 'inhibitor'
    return `${verb} the ${inhibitor}`
  }
  const tower = (building.towerType && TOWERS[building.towerType]) || 'turret'
  const where = lane ? ', ' + lane : ''
  return `${verb} the ${tower}${where}`
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
  let label: string
  switch (swing.kind) {
    case 'kill': {
      const assists = swing.assists > 0 ? ` (+${swing.assists} assist${swing.assists > 1 ? 's' : ''})` : ''
      label = `${side} ${context.championName(swing.killerId)} killed ${context.championName(swing.victimId)}${assists}`
      break
    }
    case 'turret':
    case 'inhibitor':
      label = structureLabel(swing.kind, { ours, lane: swing.lane, towerType: swing.towerType })
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
