import type { StaticItemData } from '#shared/types/static-data'
import type { CompositionBuildRequest } from '~/types/build'
import type { GamePlayer, GameState } from '~/types/game'

/** `POST /champions/{id}/next-item` (#1749), as the app sends it. */
export interface NextItemRequest {
  position: string
  /** Items held or completed, in the order they appeared in the inventory. */
  items: number[]
  allies: { championId: number, position: string | null }[]
  enemies: { championId: number, position: string | null }[]
}

export interface NextItemReason {
  axis: string
  bucket: 'High' | 'Low'
  /** Factor the situation applied to the item's share (1.4 = forty percent more often). */
  factor: number
}

export interface NextItemCandidate {
  itemId: number
  /** Predicted share of the branch in this game. */
  share: number
  /** Share of the branch whatever the game. */
  baseShare: number
  games: number
  wins: number
  reasons: NextItemReason[]
}

export interface NextItemSlot {
  parentItemId: number
  /** False when the build left the mains' tree and the answer continues from its latest known item. */
  onTree: boolean
  branchGames: number
  patchWindow: number
  candidates: NextItemCandidate[]
}

export interface NextItemResponse {
  championId: number
  position: string
  /** Null when the champion has no model at this lane. */
  patch: string | null
  build: NextItemSlot | null
  boots: NextItemSlot | null
  situation: Record<string, string>
}

/**
 * The game as the endpoint takes it, from our side. Null when the request
 * cannot be made: spectating, a queue without lanes, or a champion Data
 * Dragon has not named yet.
 */
export function nextItemRequest(
  game: GameState,
  order: number[],
  championIdOf: (alias: string) => number | null,
): { championId: number, body: NextItemRequest } | null {
  const me = game.players.find(player => player.isMe)
  if (!me || !me.position) return null
  const championId = championIdOf(me.champion)
  if (!championId) return null

  const held = me.items.map(item => item.itemId)
  const participant = (player: GamePlayer) => {
    const id = championIdOf(player.champion)
    return id ? { championId: id, position: player.position || null } : null
  }
  const others = (ours: boolean) => game.players
    .filter(player => !player.isMe && (player.team === me.team) === ours)
    .map(participant)
    .filter(entry => entry !== null)

  return {
    championId,
    body: {
      position: me.position,
      // What was completed, in order, then anything held the order missed.
      items: [...order, ...held.filter(id => !order.includes(id))],
      allies: others(true),
      enemies: others(false),
    },
  }
}

/**
 * The game as the composition build takes it (`POST /champions/{id}/composition-build`),
 * from our side — what the starter is read from. Players the game names no
 * lane for are left out: the endpoint places every pick on one. Null where
 * `nextItemRequest` is.
 */
export function starterRequest(
  game: GameState,
  championIdOf: (alias: string) => number | null,
): { championId: number, body: CompositionBuildRequest } | null {
  const me = game.players.find(player => player.isMe)
  if (!me || !me.position) return null
  const championId = championIdOf(me.champion)
  if (!championId) return null

  const side = (ours: boolean) => game.players
    .filter(player => !player.isMe && player.position && (player.team === me.team) === ours)
    .map(player => ({ championId: championIdOf(player.champion), position: player.position }))
    .filter((slot): slot is { championId: number, position: string } => slot.championId !== null)

  return { championId, body: { position: me.position, allies: side(true), enemies: side(false) } }
}

/**
 * Whether the starter is behind us: the inventory holds an item that is
 * neither a consumable nor a trinket — the starter advised, another one, or
 * anything bought on a later back. Potions alone and the trinket every player
 * spawns with are not one. An item the static data does not know yet says
 * nothing either way.
 */
export function starterBought(held: number[], items: Record<number, StaticItemData>): boolean {
  return held.some((id) => {
    const tags = items[id]?.tags
    return tags !== undefined && !tags.includes('Consumable') && !tags.includes('Trinket')
  })
}

/** The starter's price: each item of the basket at its shop price, as many times as the basket lists it. */
export function starterCost(itemIds: number[], items: Record<number, StaticItemData>): number {
  return itemIds.reduce((sum, id) => sum + (items[id]?.totalGold ?? 0), 0)
}

/** A basket in one line: each item once, with how many when the basket lists it more than once — "Doran's Blade + Health Potion ×2". */
export function basketName(itemIds: number[], name: (itemId: number) => string): string {
  const counts = new Map<number, number>()
  for (const id of itemIds) counts.set(id, (counts.get(id) ?? 0) + 1)
  return [...counts].map(([id, count]) => (count > 1 ? `${name(id)} ×${count}` : name(id))).join(' + ')
}

/** What changes the answer: any player's items, and who is on which lane. */
export function nextItemSignature(game: GameState): string {
  return game.players
    .map(player => `${player.champion}:${player.position}:${player.items.map(item => item.itemId).join('.')}`)
    .join('|')
}

/**
 * Appends to `order` the items that appeared in the inventory since the last
 * reading. The game reports slots, not when an item was completed, so the
 * order is noted here as the items land — which is what lets the endpoint
 * walk the build tree edge by edge.
 */
export function noteNewItems(order: number[], held: number[]): number[] {
  const added = held.filter(id => !order.includes(id))
  return added.length === 0 ? order : [...order, ...added]
}

/**
 * Gold still to spend before `itemId` is complete, given what the inventory
 * holds — the shop's own arithmetic: a held component counts at its full
 * price, a missing one at what it costs to build from what is held.
 */
export function goldToComplete(
  itemId: number,
  held: number[],
  items: Record<number, StaticItemData>,
): number {
  const pool = [...held]
  function remaining(id: number): number {
    const index = pool.indexOf(id)
    if (index !== -1) {
      pool.splice(index, 1)
      return 0
    }
    const item = items[id]
    if (!item) return 0
    const parts = item.from ?? []
    const combine = item.totalGold - parts.reduce((sum, part) => sum + (items[part]?.totalGold ?? 0), 0)
    return Math.max(0, combine) + parts.reduce((sum, part) => sum + remaining(part), 0)
  }
  return remaining(itemId)
}

/** One step towards an item: a component not yet held, and what it costs from here. */
export interface BuyStep {
  itemId: number
  cost: number
}

/**
 * The components of `itemId` still to buy, each at what it costs given the
 * inventory, cheapest first. The whole item when nothing of it is held yet
 * and it has no recipe.
 */
export function stepsToward(
  itemId: number,
  held: number[],
  items: Record<number, StaticItemData>,
): BuyStep[] {
  const parts = items[itemId]?.from ?? []
  if (parts.length === 0) return [{ itemId, cost: goldToComplete(itemId, held, items) }]

  const pool = [...held]
  const steps: BuyStep[] = []
  for (const part of parts) {
    const index = pool.indexOf(part)
    if (index !== -1) {
      pool.splice(index, 1)
      continue
    }
    steps.push({ itemId: part, cost: goldToComplete(part, pool, items) })
  }
  return steps.sort((a, b) => a.cost - b.cost)
}
