import type { BuildRunePage } from './champions/builds'

/**
 * A kit (#2185): a player's own build on one champion and position — a branching item tree, a rune page and two
 * summoner spells — kept by the desktop app and used in champion select and the overlay. Here rather than in the
 * app so the site can read one later (sharing a kit is a follow-up).
 */

/** A kit's rune page: the build's page without its measured figures. */
export type KitRunePage = Omit<BuildRunePage, 'games' | 'pickRate' | 'winRate'>

/** One step of a kit's item tree: an item, and the items that can follow it, the first one the default. */
export interface KitNode {
  itemId: number
  children: KitNode[]
}

export interface Kit {
  id: string
  name: string
  championId: number
  /** The position it is for (`MIDDLE`, …), as the build view names it. */
  position: string
  runes: KitRunePage
  /** The two summoner spells, Data Dragon's numeric keys (4 = Flash). */
  spells: [number, number]
  /** The starter basket, in buying order; an item twice is two of it. */
  starter: number[]
  /** The boots to pick between, the first the default; empty for none. They take one slot of a full build. */
  boots: number[]
  /** The first items, each the root of its branches; a path counts at most six items with the boots. */
  tree: KitNode[]
  /** ISO 8601, set on every save. */
  updatedAt: string
}

/** Every kit of the player's, and the one used for each champion and position (`{championId}:{position}` → kit id). */
export interface KitStore {
  kits: Kit[]
  active: Record<string, string>
}
