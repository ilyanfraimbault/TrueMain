import type { StaticItemData } from '#shared/types/static-data'
import type { GamePlayer, GameState, GameTeam, TeamObjectives } from '~/types/game'
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
 * The win-probability formula's weights, in log-odds — the product owner's
 * call (2026-10-02), a formula rather than a measured model. The item-gold
 * lead counts relative to the gold the two teams hold on average, so the same
 * gap weighs more early than late: a lead of a tenth of it alone reads about
 * 65 %. The map adds to it: three turrets ahead about +9 points from even, an
 * inhibitor down about +12, the Baron's buff about +21, the Elder's about +25.
 */
const WEIGHTS = {
  /** Per unit of item-gold lead over the teams' average item gold. */
  gold: 6,
  /** Per enemy turret destroyed beyond the other side's count. */
  turret: 0.12,
  /** Per enemy inhibitor down right now. */
  inhibitor: 0.5,
  /** Per elemental drake beyond the other side's count. */
  dragon: 0.15,
  /** On top, for the side on four drakes: the soul. */
  soul: 0.6,
  /** While the side holds the Baron's buff. */
  baron: 0.9,
  /** While the side holds the Elder's buff. */
  elder: 1.1,
} as const

/** Drakes for the dragon soul. */
const SOUL = 4

/** What one side has on the map at a given moment. */
function mapEdge(team: TeamObjectives, clock: number): number {
  const inhibitors = team.inhibitors.filter(respawn => clock < respawn).length
  return WEIGHTS.turret * team.turrets
    + WEIGHTS.inhibitor * inhibitors
    + WEIGHTS.dragon * team.dragons
    + (team.dragons >= SOUL ? WEIGHTS.soul : 0)
    + (team.baronUntil !== null && clock < team.baronUntil ? WEIGHTS.baron : 0)
    + (team.elderUntil !== null && clock < team.elderUntil ? WEIGHTS.elder : 0)
}

/**
 * The left team's chance to win (`leftTeam`) at `clock` (game seconds):
 * a logistic of its item-gold lead and of what each side holds on the map —
 * turrets, inhibitors down, drakes and the soul, the Baron's and the Elder's
 * buffs while they last. Even (0.5) at the start.
 */
export function winProbability(game: GameState, items: Record<number, StaticItemData>, clock: number): number {
  const left = leftTeam(game)
  const gold = (team: GameTeam) => game.players
    .filter(player => player.team === team)
    .reduce((sum, player) => sum + itemGold(player, items), 0)
  const ours = gold(left)
  const theirs = gold(left === 'ORDER' ? 'CHAOS' : 'ORDER')
  const average = (ours + theirs) / 2
  const lead = average > 0 ? (ours - theirs) / average : 0

  const side = (team: GameTeam) => (team === 'ORDER' ? game.objectives.order : game.objectives.chaos)
  const map = mapEdge(side(left), clock) - mapEdge(side(left === 'ORDER' ? 'CHAOS' : 'ORDER'), clock)

  return 1 / (1 + Math.exp(-(WEIGHTS.gold * lead + map)))
}
