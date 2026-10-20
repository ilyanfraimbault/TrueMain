import type { GameObjectives, GameTeam, ObjectiveSpawn } from '~/types/game'

/**
 * The objectives panel's pills (#2078), from the timers the shell derives
 * (`crates/live-client/src/timers.rs`) and the clock the page runs between
 * two readings. The shell sends game times, never countdowns, so the pills
 * count down here and move only when an objective falls.
 */

export type ObjectiveKey = 'dragon' | 'grubs' | 'herald' | 'baron'

/** Where an objective stands at a moment of the game. */
export type ObjectiveStatus =
  | { kind: 'countdown', seconds: number }
  | { kind: 'up' }
  /** It will not come again: taken for good (by `by`, if known), or left the map. */
  | { kind: 'done', by: GameTeam | null }

/** A countdown this short or shorter reads as "soon". */
export const SOON_SECONDS = 60

export function objectiveStatus(spawn: ObjectiveSpawn, clock: number): ObjectiveStatus {
  if (spawn.at === null || (spawn.until !== null && clock >= spawn.until)) return { kind: 'done', by: spawn.at === null ? spawn.takenBy : null }
  if (clock < spawn.at) return { kind: 'countdown', seconds: Math.ceil(spawn.at - clock) }
  return { kind: 'up' }
}

/** `m:ss`, counting a started second as a whole one so the pill turns "Up" as it reaches 0:00. */
export function formatCountdown(seconds: number): string {
  const whole = Math.max(0, Math.ceil(seconds))
  return `${Math.floor(whole / 60)}:${`${whole % 60}`.padStart(2, '0')}`
}

/** Drakes that give a team the soul — `RIFT.soul_drakes` in the shell. */
export const SOUL_DRAKES = 4

/** A side's drakes, and how close they are to the soul. */
export interface DrakeStanding {
  count: number
  /** One drake from the soul, while no team holds it. */
  soulPoint: boolean
  soul: boolean
}

/** Each side's drakes, ours (or blue side when spectating) first. */
export function drakeStandings(objectives: GameObjectives, left: GameTeam): { ours: DrakeStanding, theirs: DrakeStanding } {
  const soulTaken = objectives.order.dragons >= SOUL_DRAKES || objectives.chaos.dragons >= SOUL_DRAKES
  const standing = (count: number): DrakeStanding => ({
    count,
    soulPoint: !soulTaken && count === SOUL_DRAKES - 1,
    soul: count >= SOUL_DRAKES,
  })
  const order = standing(objectives.order.dragons)
  const chaos = standing(objectives.chaos.dragons)
  return left === 'ORDER' ? { ours: order, theirs: chaos } : { ours: chaos, theirs: order }
}
