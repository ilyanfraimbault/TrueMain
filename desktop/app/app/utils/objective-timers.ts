import type { DrakeElement, NeutralTimers, ObjectiveSpawn } from '~/types/game'

/**
 * The objectives strip's pills (#2078, #2184), from the timers the shell derives
 * (`crates/live-client/src/timers.rs`) and the clock the page runs between
 * two readings. The shell sends game times, never countdowns, so the pills
 * count down here and move only when an objective falls.
 */

export type ObjectiveKey = 'dragon' | 'grubs' | 'herald' | 'baron'

/** Where an objective stands at a moment of the game. */
export type ObjectiveStatus =
  | { kind: 'countdown', seconds: number }
  | { kind: 'up' }
  /** It will not come again: taken for good, or left the map. */
  | { kind: 'done' }

/** A countdown this short or shorter reads as "soon". */
export const SOON_SECONDS = 60

/** The game's own minimap icons, from Community Dragon (the CSP lets them through). */
const MINIMAP_ICONS = 'https://raw.communitydragon.org/latest/game/assets/ux/minimap/icons'

/** A drake's minimap icon. */
export function drakeIcon(element: DrakeElement): string {
  return `${MINIMAP_ICONS}/dragon_${element}.png`
}

/** The minimap icon of an objective; the dragon pit's is its next drake's element, or the Elder's. */
export function objectiveIcon(key: ObjectiveKey, timers: Pick<NeutralTimers, 'elder' | 'drake'>): string {
  const name = {
    dragon: timers.elder ? 'dragon_elder' : timers.drake ? `dragon_${timers.drake}` : 'dragon',
    grubs: 'grub',
    herald: 'riftherald',
    baron: 'baron',
  }[key]
  return `${MINIMAP_ICONS}/${name}.png`
}

const DRAKE_NAMES: Record<DrakeElement, string> = {
  infernal: 'Infernal',
  ocean: 'Ocean',
  mountain: 'Mountain',
  cloud: 'Cloud',
  hextech: 'Hextech',
  chemtech: 'Chemtech',
}

/** The dragon pit's name: the Elder, the next drake's element, or "Drake" while the API does not name it. */
export function dragonLabel(timers: Pick<NeutralTimers, 'elder' | 'drake'>): string {
  if (timers.elder) return 'Elder'
  return timers.drake ? DRAKE_NAMES[timers.drake] : 'Drake'
}

export function objectiveStatus(spawn: ObjectiveSpawn, clock: number): ObjectiveStatus {
  if (spawn.at === null || (spawn.until !== null && clock >= spawn.until)) return { kind: 'done' }
  if (clock < spawn.at) return { kind: 'countdown', seconds: Math.ceil(spawn.at - clock) }
  return { kind: 'up' }
}

/** The camp the top side holds before each: the Herald comes where the grubs were, Baron after the Herald. */
const BEHIND: Partial<Record<ObjectiveKey, ObjectiveKey>> = { herald: 'grubs', baron: 'herald' }

/** A pill's state: its objective's status, or `waiting` while it does not matter yet. */
export type PillStatus = ObjectiveStatus | { kind: 'waiting', seconds: number }

/**
 * Where a pill stands: as its objective, except that a countdown waits — drawn
 * in grey — while the camp the top side holds before it is still to come or
 * stands: the Herald until the grubs are gone, Baron until the Herald is — at
 * 19:45 at the latest, a quarter of a minute before Baron's first spawn.
 */
export function pillStatus(key: ObjectiveKey, timers: NeutralTimers, clock: number): PillStatus {
  const status = objectiveStatus(timers[key], clock)
  const before = BEHIND[key]
  if (status.kind === 'countdown' && before && objectiveStatus(timers[before], clock).kind !== 'done') return { kind: 'waiting', seconds: status.seconds }
  return status
}

/** `m:ss`, counting a started second as a whole one so the countdown gives way to "up" as it reaches 0:00. */
export function formatCountdown(seconds: number): string {
  const whole = Math.max(0, Math.ceil(seconds))
  return `${Math.floor(whole / 60)}:${`${whole % 60}`.padStart(2, '0')}`
}
