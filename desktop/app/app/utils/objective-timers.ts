import type { DrakeElement, NeutralTimers, ObjectiveSpawn } from '~/types/game'

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

/** `m:ss`, counting a started second as a whole one so the countdown gives way to "up" as it reaches 0:00. */
export function formatCountdown(seconds: number): string {
  const whole = Math.max(0, Math.ceil(seconds))
  return `${Math.floor(whole / 60)}:${`${whole % 60}`.padStart(2, '0')}`
}
