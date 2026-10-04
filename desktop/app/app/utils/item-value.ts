import type { StaticItemData } from '#shared/types/static-data'
import type { GamePlayer, GameState, GameTeam, TeamObjectives } from '~/types/game'
import { LANES, type Lane } from '~/types/draft'

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
 * The win-probability model's weights, in log-odds, fitted on TrueMain's own
 * ranked games (2026-10-04, about a million per minute mark): a logistic
 * regression of the win on each lane's creep-score, level and kill lead over
 * the opposite lane, at 5, 10, 15, 20 and 30 minutes. Only what the game's API
 * shows for all ten players goes in — it gives nobody's gold but ours, and
 * item gold lags behind the side ahead (the side behind shops each time it
 * dies). The support's lead weighs little: its kills about half a carry's,
 * its creep score nothing (a support farming takes from the carry), its gold
 * measured at about a third of a carry's per thousand.
 *
 * Per lane, in `LANES` order: per ten creep score, per level, per kill.
 */
const MINUTES = [5, 10, 15, 20, 30] as const
const LEAD_WEIGHTS: Record<Lane, { cs: number[], level: number[], kill: number[] }> = {
  TOP: { cs: [0.116, 0.074, 0.052, 0.049, 0.049], level: [0.033, 0.079, 0.123, 0.184, 0.257], kill: [0.206, 0.15, 0.109, 0.086, 0.055] },
  JUNGLE: { cs: [0.269, 0.178, 0.101, 0.064, 0.035], level: [0.045, 0.073, 0.149, 0.214, 0.279], kill: [0.269, 0.193, 0.135, 0.1, 0.06] },
  MIDDLE: { cs: [0.159, 0.115, 0.088, 0.065, 0.042], level: [0.039, 0.095, 0.163, 0.212, 0.264], kill: [0.243, 0.175, 0.112, 0.08, 0.054] },
  BOTTOM: { cs: [0.202, 0.149, 0.112, 0.095, 0.062], level: [0.023, 0.056, 0.13, 0.196, 0.266], kill: [0.229, 0.173, 0.133, 0.111, 0.079] },
  UTILITY: { cs: [-0.037, -0.039, -0.046, -0.033, 0.001], level: [0.04, 0.065, 0.168, 0.258, 0.341], kill: [0.127, 0.079, 0.033, 0.017, 0.021] },
}

/** A weight at `clock` (game seconds): linear between two marks, the nearest one outside them. */
function at(weights: number[], clock: number): number {
  const minute = clock / 60
  const first = MINUTES[0]
  const last = MINUTES[MINUTES.length - 1]!
  if (minute <= first) return weights[0]!
  if (minute >= last) return weights[weights.length - 1]!
  const upper = MINUTES.findIndex(mark => mark >= minute)
  const from = MINUTES[upper - 1]!
  const to = MINUTES[upper]!
  return weights[upper - 1]! + (weights[upper]! - weights[upper - 1]!) * (minute - from) / (to - from)
}

/**
 * The map's weights, in log-odds — the product owner's call (2026-10-02), not
 * measured: the stored timelines keep no objectives. Three turrets ahead are
 * about +9 points from even, an inhibitor down about +12, the Baron's buff
 * about +21, the Elder's about +25.
 */
const MAP_WEIGHTS = {
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
  return MAP_WEIGHTS.turret * team.turrets
    + MAP_WEIGHTS.inhibitor * inhibitors
    + MAP_WEIGHTS.dragon * team.dragons
    + (team.dragons >= SOUL ? MAP_WEIGHTS.soul : 0)
    + (team.baronUntil !== null && clock < team.baronUntil ? MAP_WEIGHTS.baron : 0)
    + (team.elderUntil !== null && clock < team.elderUntil ? MAP_WEIGHTS.elder : 0)
}

/** Each lane's lead, ours over theirs, weighed at `clock`; a lane missing a side counts nothing. */
function laneEdge(game: GameState, clock: number): number {
  let edge = 0
  for (const { ally, enemy } of laneRows(game)) {
    if (!ally || !enemy) continue
    const weights = LEAD_WEIGHTS[ally.position as Lane]
    if (!weights) continue
    edge += at(weights.cs, clock) * (ally.creepScore - enemy.creepScore) / 10
      + at(weights.level, clock) * (ally.level - enemy.level)
      + at(weights.kill, clock) * (ally.kills - enemy.kills)
  }
  return edge
}

/**
 * The left team's chance to win (`leftTeam`) at `clock` (game seconds):
 * a logistic of each lane's lead (creep score, level, kills — `LEAD_WEIGHTS`)
 * and of what each side holds on the map — turrets, inhibitors down, drakes
 * and the soul, the Baron's and the Elder's buffs while they last. Even (0.5)
 * at the start. In a queue without lanes only the map counts.
 */
export function winProbability(game: GameState, clock: number): number {
  const left = leftTeam(game)
  const side = (team: GameTeam) => (team === 'ORDER' ? game.objectives.order : game.objectives.chaos)
  const map = mapEdge(side(left), clock) - mapEdge(side(left === 'ORDER' ? 'CHAOS' : 'ORDER'), clock)
  return 1 / (1 + Math.exp(-(laneEdge(game, clock) + map)))
}
