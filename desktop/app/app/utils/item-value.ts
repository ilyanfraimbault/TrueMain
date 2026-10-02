import type { StaticItemData } from '#shared/types/static-data'
import type { GamePlayer, GameState, GameTeam } from '~/types/game'
import { LANES } from '~/types/draft'

/**
 * What the items in a player's inventory cost — the arithmetic the scoreboard
 * does not do (#1752). Each item at its full price (Data Dragon `gold.total`),
 * a component at its own; consumables and trinkets are left out, since they
 * would make the figure jump for nothing (a control ward bought, a potion
 * drunk). Only what the scoreboard itself shows goes in.
 */
export function itemGold(player: GamePlayer, items: Record<number, StaticItemData>): number {
  let total = 0
  for (const held of player.items) {
    const item = items[held.itemId]
    if (!item) continue
    if (item.tags?.includes('Consumable') || item.tags?.includes('Trinket')) continue
    total += item.totalGold
  }
  return total
}

/** Our side, or blue side when spectating: always drawn on the left. */
export function leftTeam(game: GameState): GameTeam {
  return game.myTeam ?? 'ORDER'
}

const laneIndex = (position: string) => {
  const index = (LANES as readonly string[]).indexOf(position)
  return index === -1 ? LANES.length : index
}

/** A side in lane order; a player on no lane keeps the game's order after the five. */
export function sideInLaneOrder(game: GameState, team: GameTeam): GamePlayer[] {
  return game.players
    .filter(player => player.team === team)
    .map((player, order) => ({ player, order }))
    .sort((a, b) => laneIndex(a.player.position) - laneIndex(b.player.position) || a.order - b.order)
    .map(({ player }) => player)
}

/** Lane by lane, ours against theirs: each row a lane's face-off. */
export function laneRows(game: GameState): { ally: GamePlayer | null, enemy: GamePlayer | null }[] {
  const left = leftTeam(game)
  const ours = sideInLaneOrder(game, left)
  const theirs = sideInLaneOrder(game, left === 'ORDER' ? 'CHAOS' : 'ORDER')
  return Array.from({ length: Math.max(ours.length, theirs.length) }, (_, index) => ({
    ally: ours[index] ?? null,
    enemy: theirs[index] ?? null,
  }))
}

/**
 * How sharply the estimate turns a lead into a probability. The product
 * owner's call (2026-10-02): an estimate from the item-gold gap alone, not a
 * measured model — a lead worth a tenth of the gold the two teams hold on
 * average reads about 65 %, a fifth about 77 %.
 */
const WIN_STEEPNESS = 6

/**
 * The left team's chance to win, estimated from the item-gold gap relative to
 * what the teams hold — so 2000 gold weighs more at ten minutes than at
 * thirty. Even (0.5) before anyone has bought anything.
 */
export function winProbability(ours: number, theirs: number): number {
  const average = (ours + theirs) / 2
  if (average <= 0) return 0.5
  const lead = (ours - theirs) / average
  return 1 / (1 + Math.exp(-WIN_STEEPNESS * lead))
}
