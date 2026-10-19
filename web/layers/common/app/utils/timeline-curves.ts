import type {
  WinProbabilityTimeline,
  WinProbabilityTimelinePlayer,
} from '#shared/types/win-probability'
import { formatCount } from '#shared/utils/counts'
import { formatKilo } from './match-detail-format'

/**
 * The ten players' gold, experience and creep score over a finished game, and
 * the team lead under them (#2102) — the match detail's Timeline view, in the
 * desktop app only. Read from the same client timeline as the win probability
 * (`WinProbabilityTimeline`): one frame a minute, plus the last one at the end
 * of the game. The site stores timelines at a few marks only (#772), so it
 * never has these curves.
 */

export type CurveMetric = 'gold' | 'xp' | 'cs'

export const CURVE_METRICS: { key: CurveMetric, label: string, unit: string }[] = [
  { key: 'gold', label: 'Gold', unit: 'gold' },
  { key: 'xp', label: 'XP', unit: 'XP' },
  { key: 'cs', label: 'CS', unit: 'CS' },
]

export interface CurvePoint {
  ms: number
  value: number
}

export interface PlayerCurve {
  participantId: number
  teamId: number
  points: CurvePoint[]
  /** The value at the last frame. */
  end: number
}

export interface TimelineCurves {
  /** The game time the x axis spans, ms. */
  durationMs: number
  players: PlayerCurve[]
  /** The perspective side's total minus the other side's, at every frame. */
  lead: CurvePoint[]
}

export interface TeamCurves {
  teamId: number
  /** The team's total at the last frame. */
  total: number
  /** Its players, highest end value first. */
  players: PlayerCurve[]
}

function read(player: WinProbabilityTimelinePlayer | undefined, metric: CurveMetric): number | null {
  if (!player) return null
  return player[metric] ?? null
}

/**
 * Whether the timeline carries the curves: two frames at least, with gold on
 * them. A timeline kept before #2102 has none (a recording's), or zeros where
 * the shell defaulted the missing figure — never a flat curve at zero.
 */
export function hasTimelineCurves(timeline: WinProbabilityTimeline | null | undefined): timeline is WinProbabilityTimeline {
  if (!timeline || timeline.frames.length < 2) return false
  return timeline.frames.some(frame => frame.players.some(player => (player.gold ?? 0) > 0))
}

/**
 * Every player's curve for `metric` and the lead read for `perspectiveTeamId`;
 * null when the timeline carries no curves. A frame missing a player holds
 * their previous value: the three figures only ever grow.
 */
export function buildTimelineCurves(
  timeline: WinProbabilityTimeline | null | undefined,
  metric: CurveMetric,
  perspectiveTeamId: number,
): TimelineCurves | null {
  if (!hasTimelineCurves(timeline)) return null
  const frames = [...timeline.frames].sort((a, b) => a.ms - b.ms)

  const players = timeline.participants.map((participant) => {
    let last = 0
    const points = frames.map((frame) => {
      const value = read(frame.players.find(p => p.participantId === participant.participantId), metric)
      if (value !== null) last = value
      return { ms: frame.ms, value: last }
    })
    return { participantId: participant.participantId, teamId: participant.teamId, points, end: last }
  })

  const lead = frames.map((frame, index) => ({
    ms: frame.ms,
    value: players.reduce((sum, player) => {
      const value = player.points[index]!.value
      return sum + (player.teamId === perspectiveTeamId ? value : -value)
    }, 0),
  }))

  return {
    durationMs: Math.max(timeline.durationMs, frames.at(-1)!.ms),
    players,
    lead,
  }
}

/** Both sides with their totals, the perspective side first. */
export function teamCurves(curves: TimelineCurves, perspectiveTeamId: number): [TeamCurves, TeamCurves] {
  const side = (ours: boolean): TeamCurves => {
    const players = curves.players
      .filter(player => (player.teamId === perspectiveTeamId) === ours)
      .sort((a, b) => b.end - a.end)
    return {
      teamId: ours ? perspectiveTeamId : (players[0]?.teamId ?? 0),
      total: players.reduce((sum, player) => sum + player.end, 0),
      players,
    }
  }
  return [side(true), side(false)]
}

/** The round figures an axis may top out at, × 10ⁿ: fine enough to waste little height. */
const NICE_STEPS = [1, 1.2, 1.5, 2, 2.5, 3, 4, 5, 6, 8, 10]

/**
 * The top of a value axis: the smallest round figure (`NICE_STEPS` × 10ⁿ) at
 * or above `max`, so the top and middle gridlines read as round numbers. At
 * least 1.
 */
export function niceCeiling(max: number): number {
  if (!Number.isFinite(max) || max <= 1) return 1
  const magnitude = 10 ** Math.floor(Math.log10(max))
  const step = NICE_STEPS.find(candidate => candidate * magnitude >= max)!
  return step * magnitude
}

/** A figure as the panel prints it: `12.0k` gold or XP, `227` CS. */
export function formatCurveValue(value: number, metric: CurveMetric): string {
  return metric === 'cs' ? formatCount(Math.round(value)) : formatKilo(Math.round(value))
}

/** A signed figure: `+4.3k`, `−12`, and `0` without a sign. */
export function formatCurveDelta(value: number, metric: CurveMetric): string {
  const text = formatCurveValue(Math.abs(value), metric)
  if (Math.round(value) === 0) return text
  return `${value > 0 ? '+' : '−'}${text}`
}

/** The header's verdict: `Your team +4.3k gold`, `Your team −12 CS`, `Teams even on XP`. */
export function formatTeamDifference(difference: number, metric: CurveMetric): string {
  const unit = CURVE_METRICS.find(entry => entry.key === metric)!.unit
  if (Math.round(difference) === 0) return `Teams even on ${unit}`
  return `Your team ${formatCurveDelta(difference, metric)} ${unit}`
}

/** The time axis' minute labels: every 5 minutes, every 10 past half an hour. */
export function minuteTicks(widthMs: number): { minute: number, ms: number }[] {
  const total = Math.floor(widthMs / 60_000)
  const step = total > 30 ? 10 : 5
  return Array.from({ length: Math.floor(total / step) + 1 }, (_, i) => ({ minute: i * step, ms: i * step * 60_000 }))
}
