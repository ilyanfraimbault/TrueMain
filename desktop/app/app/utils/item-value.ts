import type { StaticItemData } from '#shared/types/static-data'
import type { GamePlayer, GameState, GameTeam, TeamObjectives } from '~/types/game'
import {
  LEAD_WEIGHTS,
  winProbability as modelWinProbability,
  type LaneLead,
  type SideMap,
  type WinProbabilityLane,
} from '#common/utils/win-probability'
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

/** What one side holds on the map at `clock`, in the model's terms. */
function sideMap(team: TeamObjectives, clock: number): SideMap {
  return {
    turrets: team.turrets,
    inhibitorsDown: team.inhibitors.filter(respawn => clock < respawn).length,
    dragons: team.dragons,
    baron: team.baronUntil !== null && clock < team.baronUntil,
    elder: team.elderUntil !== null && clock < team.elderUntil,
  }
}

/** Each lane's lead, ours over theirs; a lane missing a side counts nothing. */
function laneLeads(game: GameState): LaneLead[] {
  const leads: LaneLead[] = []
  for (const { ally, enemy } of laneRows(game)) {
    if (!ally || !enemy || !(ally.position in LEAD_WEIGHTS)) continue
    leads.push({
      lane: ally.position as WinProbabilityLane,
      cs: ally.creepScore - enemy.creepScore,
      level: ally.level - enemy.level,
      kills: ally.kills - enemy.kills,
    })
  }
  return leads
}

/**
 * The left team's chance to win (`leftTeam`) at `clock` (game seconds), from
 * the shared model (`#common/utils/win-probability`): each lane's lead
 * (creep score, level, kills) and what each side holds on the map — turrets,
 * inhibitors down, drakes and the soul, the Baron's and the Elder's buffs
 * while they last. Even (0.5) at the start. In a queue without lanes only the
 * map counts.
 */
export function winProbability(game: GameState, clock: number): number {
  const left = leftTeam(game)
  const side = (team: GameTeam) => (team === 'ORDER' ? game.objectives.order : game.objectives.chaos)
  return modelWinProbability({
    leads: laneLeads(game),
    ours: sideMap(side(left), clock),
    theirs: sideMap(side(left === 'ORDER' ? 'CHAOS' : 'ORDER'), clock),
    clock,
  })
}
