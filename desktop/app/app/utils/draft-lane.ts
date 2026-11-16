import type { DraftState, TeamSlot } from '~/types/lcu'
import { POSITIONS } from '#shared/utils/positions'

/**
 * The lane our allies leave us, when there is exactly one: the client assigns
 * no lane to the player in blind pick or a custom game, while the bots of a
 * custom lobby carry the lane they were given (#2461). Null when two lanes or
 * more are still open — a guess there would rank for the wrong lane.
 */
export function freeLane(team: TeamSlot[]): string | null {
  if (team.length !== POSITIONS.length) return null
  const held = new Set(team.filter(slot => !slot.isMe).map(slot => slot.position))
  const open = POSITIONS.filter(lane => !held.has(lane))
  return open.length === 1 ? open[0]! : null
}

/** The draft as if the client had given us `lane`: our position and our own slot's. */
export function withLane(draft: DraftState, lane: string): DraftState {
  if (draft.myPosition === lane) return draft
  return {
    ...draft,
    myPosition: lane,
    myTeam: draft.myTeam.map(slot => (slot.isMe ? { ...slot, position: lane } : slot)),
  }
}

/** Our side holds the five lanes once each: its cards can be laid out lane by lane. */
export function holdsEveryLane(team: TeamSlot[]): boolean {
  const held = new Set(team.map(slot => slot.position))
  return team.length === POSITIONS.length && POSITIONS.every(lane => held.has(lane))
}
