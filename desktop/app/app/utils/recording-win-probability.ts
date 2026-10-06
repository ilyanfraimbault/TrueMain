import type { WinProbabilitySwingKind } from '#shared/types/win-probability'
import type { MomentKind, RecordingWinProbability } from '~/types/recordings'
import { buildWinProbability } from '#common/utils/win-probability-timeline'
import { describeSwing, forTeam, SWINGS_SHOWN, type DescribedSwing } from '#common/utils/win-probability-swing'

/**
 * A recorded game's win-probability curve and turning points (#1911), placed
 * on its video: the shared model (`buildWinProbability`) run on the timeline
 * the recording kept, read for the player's side, every game time moved onto
 * the video the way the shell places the moments.
 */

/** A point of the curve on the video: the player's side's chance, 0..1. */
export interface RecapCurvePoint {
  videoMs: number
  p: number
}

/** A turning point, from the player's side, on the video. */
export interface RecapTurningPoint extends DescribedSwing {
  gameMs: number
  videoMs: number
  /** Its glyph, from the moments' set. */
  icon: MomentKind
}

export interface RecapWinProbability {
  curve: RecapCurvePoint[]
  /** The largest swings, largest first. */
  turningPoints: RecapTurningPoint[]
}

const ICONS: Record<WinProbabilitySwingKind, MomentKind> = {
  kill: 'kill',
  turret: 'tower',
  inhibitor: 'inhibitor',
  dragon: 'dragon',
  elder: 'elder',
  baron: 'baron',
}

/**
 * The video time of a moment of the game: the shell's `Anchor::video_ms` —
 * the last segment starting at or before it (the first one before any), its
 * offset added, never before the video's start — then clamped to the video's
 * length as `moments::on_video` clamps the moments. Change one, change both.
 */
export function videoMsAt(clock: RecordingWinProbability['clock'], gameMs: number, durationMs: number | null): number {
  const segment = [...clock].reverse().find(entry => entry.fromGameMs <= gameMs) ?? clock[0]
  const video = Math.max(gameMs + (segment?.offsetMs ?? 0), 0)
  return durationMs === null ? video : Math.min(video, durationMs)
}

/** The curve and turning points on the video; null when the game gets no curve. */
export function recapWinProbability(
  data: RecordingWinProbability,
  championName: (championId: number) => string,
): RecapWinProbability | null {
  if (!data.clock.length) return null
  const built = buildWinProbability(data.timeline)
  if (!built) return null
  const place = (gameMs: number) => videoMsAt(data.clock, gameMs, data.durationMs)
  const champions = new Map(data.champions.map(entry => [entry.participantId, entry.championId]))
  const context = {
    perspectiveTeamId: data.teamId,
    championName: (participantId: number) => {
      const championId = champions.get(participantId)
      return championId ? championName(championId) : 'Unknown'
    },
  }
  return {
    curve: built.points.map(point => ({ videoMs: place(point.ms), p: forTeam(point.p, data.teamId, 'probability') })),
    turningPoints: built.swings.slice(0, SWINGS_SHOWN).map(swing => ({
      ...describeSwing(swing, context),
      gameMs: swing.ms,
      videoMs: place(swing.ms),
      icon: ICONS[swing.kind],
    })),
  }
}

/** The curve's value at `videoMs`: linear between its points, the nearest outside them. */
export function curveAt(curve: RecapCurvePoint[], videoMs: number): number {
  if (!curve.length) return 0.5
  if (videoMs <= curve[0]!.videoMs) return curve[0]!.p
  const upper = curve.findIndex(point => point.videoMs >= videoMs)
  if (upper === -1) return curve[curve.length - 1]!.p
  const from = curve[upper - 1]!
  const to = curve[upper]!
  const span = to.videoMs - from.videoMs
  return span > 0 ? from.p + (to.p - from.p) * (videoMs - from.videoMs) / span : to.p
}
