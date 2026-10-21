import type { StaticItemData } from '#shared/types/static-data'
import type { GamePlayer, GameState, GameTeam } from '~/types/game'
import { itemGold, sideInLaneOrder } from '~/utils/item-value'

/**
 * The lane-gold plates (#2186): one per row of the game's TAB scoreboard, the
 * item-gold gap between the two players on it.
 *
 * The scoreboard lists blue side (ORDER) on the left and red side (CHAOS) on
 * the right whichever side we are on, each team in lane order — top, jungle,
 * mid, bot, support — or in the API's order in a queue without lanes. "Mirror
 * scoreboard layout" only turns the left team's rows to read inwards: the
 * teams keep their sides, so nothing here depends on it.
 */

export interface ScoreboardPair {
  left: GamePlayer | null
  right: GamePlayer | null
}

/** The scoreboard's rows, top to bottom: the blue side's player on the left, the red side's on the right. */
export function scoreboardPairs(game: GameState): ScoreboardPair[] {
  const left = sideInLaneOrder(game, 'ORDER')
  const right = sideInLaneOrder(game, 'CHAOS')
  return Array.from({ length: Math.max(left.length, right.length) }, (_, index) => ({
    left: left[index] ?? null,
    right: right[index] ?? null,
  }))
}

export interface LanePlate {
  /** The gap's size, in gold. */
  gold: number
  /** The scoreboard side ahead; null when even. */
  ahead: 'left' | 'right' | null
  /** The side ahead is ours (blue side's when spectating), for its colour. */
  ours: boolean
}

/** Which team we read as ours: our own, or blue side's when spectating. */
const oursOf = (game: GameState): GameTeam => game.myTeam ?? 'ORDER'

/** Each row's plate, top to bottom; null on a row missing a player. */
export function lanePlates(game: GameState, items: Record<number, StaticItemData>): (LanePlate | null)[] {
  const ours = oursOf(game)
  return scoreboardPairs(game).map(({ left, right }) => {
    if (!left || !right) return null
    const gap = itemGold(left, items) - itemGold(right, items)
    const ahead = gap > 0 ? 'left' : gap < 0 ? 'right' : null
    const team = gap < 0 ? right.team : left.team
    return { gold: Math.abs(gap), ahead, ours: ahead !== null && team === ours }
  })
}

/** A gap as a plate prints it: whole gold under 1,000, then thousands to one decimal under 10 (`1.5k`, `2k`, `12k`). */
export function plateGold(gold: number): string {
  if (gold < 1000) return String(Math.round(gold))
  const thousands = Math.round(gold / 100) / 10
  return `${thousands >= 10 ? Math.round(thousands) : thousands}k`
}
