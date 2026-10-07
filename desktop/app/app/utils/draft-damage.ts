import type { DamageProfile, TeamDamage } from '~/utils/damage-profile'
import { teamDamage } from '~/utils/damage-profile'

/**
 * The draft's damage reading (#1907): the team bars in champion select and the
 * note on a suggested pick, on the same rules as the damage axes of the
 * item-context fold, so what the player sees and what the build advice was
 * measured against cannot disagree. Held to the C# by a shared fixture
 * (`backend/tests/TrueMain.UnitTests/Fixtures/team-damage-share.json`).
 */

/** The damage bands of `DraftAxisThresholds` (C#), restated: the fixture test fails if either side moves. */
export const DAMAGE_THRESHOLDS = {
  enemyMagicShareLow: 0.30,
  enemyMagicShareHigh: 0.55,
  enemyPhysicalShareLow: 0.35,
  enemyPhysicalShareHigh: 0.65,
  allyMagicShareLow: 0.30,
  allyMagicShareHigh: 0.55,
} as const

export type DamageBand = 'Low' | 'Mid' | 'High'

/** `DraftAxisEvaluator.AddBand`: below the low edge is Low, at or above the high edge is High. */
export function band(value: number, low: number, high: number): DamageBand {
  return value < low ? 'Low' : value >= high ? 'High' : 'Mid'
}

/**
 * Whether a team's split may be stated, by `DraftSide.IsUsable`: something
 * measured, and at most one pick unmeasured. Two unknowns turn the share into
 * a guess, and the bar then shows no figure rather than a wrong one.
 */
export function isUsable(team: TeamDamage | null): team is TeamDamage {
  return team !== null && team.measured > 0 && team.unmeasured <= 1
}

export type DamageKind = 'physical' | 'magic' | 'true'

/** The type a team deals most of, and its share. */
export function dominant(team: TeamDamage): { kind: DamageKind, share: number } {
  const kinds: { kind: DamageKind, share: number }[] = [
    { kind: 'physical', share: team.physicalShare },
    { kind: 'magic', share: team.magicShare },
    { kind: 'true', share: team.trueShare },
  ]
  return kinds.reduce((best, next) => (next.share > best.share ? next : best))
}

/** How many measured allies a note needs: one ally's damage type is not "your team". */
export const NOTE_MIN_ALLIES = 2

export interface DamageNote {
  /** The damage the pick adds. */
  adds: 'physical' | 'magic'
  text: string
}

/**
 * What a candidate does to our team's damage mix, said only when it answers
 * a one-sided team: the team sits at one end of the ally magic-damage axis
 * and the candidate at the other. A note, never a score — the ranking stays
 * the endpoint's measured deltas (#1675); a flex champion's blended split is
 * an expectation, not an answer, so it gets none.
 */
export function damageNote(allies: (DamageProfile | null)[], candidate: DamageProfile | null): DamageNote | null {
  if (candidate?.source !== 'measured' || candidate.flexDamage || candidate.magicShare === null) return null
  const team = teamDamage(allies)
  if (!isUsable(team) || team.measured < NOTE_MIN_ALLIES) return null

  const { allyMagicShareLow: low, allyMagicShareHigh: high } = DAMAGE_THRESHOLDS
  const teamBand = band(team.magicShare, low, high)
  const pickBand = band(candidate.magicShare, low, high)
  if (teamBand === 'Low' && pickBand === 'High') return { adds: 'magic', text: 'Adds magic damage — your team is mostly physical' }
  if (teamBand === 'High' && pickBand === 'Low') return { adds: 'physical', text: 'Adds physical damage — your team is mostly magic' }
  return null
}

/** One item the draft pushes the mains toward, and the situation that does. */
export interface DraftItemPush {
  slot: 'boots' | 'build'
  itemId: number
  share: number
  baseShare: number
  reason: { axis: string, bucket: 'High' | 'Low' }
}

/** The slice of a next-item slot (`utils/next-item`) the advice reads. */
interface SlotCandidates {
  candidates: { itemId: number, share: number, baseShare: number, reasons: { axis: string, bucket: 'High' | 'Low' }[] }[]
}

/**
 * How far the draft must move an item before it is advice: five points of the
 * branch *and* a quarter more often than usual. Below that the shift is the
 * model's noise, and "21% vs 20%" read as a recommendation is worse than none.
 */
export const PUSH_MIN_GAIN = 0.05
export const PUSH_MIN_RATIO = 1.25

/**
 * The situations the draft advice may give as a reason: what the enemy team
 * and the lane opponent bring. The ally axes measure what a flex pick's team
 * already has — "build Shieldbow when your team deals magic damage" answers
 * nothing in this draft — and the gold lead does not exist before the game.
 */
export function isDraftReason(axis: string): boolean {
  return axis.startsWith('Enemy') || axis.startsWith('Opponent')
}

/**
 * The item of a slot this draft moves up most: among the candidates an enemy
 * or lane situation moved, and that the mains take clearly more often in a
 * draft like this one than usually, the one moved furthest. Null when the
 * draft moves nothing that far — the standard build already is the answer.
 */
export function draftPush(slot: 'boots' | 'build', answer: SlotCandidates | null): DraftItemPush | null {
  const best = (answer?.candidates ?? [])
    .map(candidate => ({ ...candidate, reason: candidate.reasons.find(reason => isDraftReason(reason.axis)) }))
    .filter(candidate => candidate.reason
      && candidate.share - candidate.baseShare >= PUSH_MIN_GAIN
      && candidate.share >= candidate.baseShare * PUSH_MIN_RATIO)
    .sort((a, b) => (b.share - b.baseShare) - (a.share - a.baseShare))[0]
  return best ? { slot, itemId: best.itemId, share: best.share, baseShare: best.baseShare, reason: { axis: best.reason!.axis, bucket: best.reason!.bucket } } : null
}
