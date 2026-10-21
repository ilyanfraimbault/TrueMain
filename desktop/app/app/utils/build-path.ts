import type { StaticItemData } from '#shared/types/static-data'

/** Our gold reaches the page floored to this (`GOLD_STEP` in `crates/live-client/src/game.rs`). */
export const GOLD_STEP = 50

/**
 * One component on the way to an item (#2183), as `POST /champions/{id}/next-item`
 * serves it on each candidate: in the order the champion's mains buy them, or
 * the recipe's when too few games measured it (`measured: false`).
 */
export interface ItemRouteStep {
  itemId: number
  /** The component's full shop price. */
  cost: number
  /** 1 for the component bought first. */
  rank: number
  /** Share of the measured games that bought it at this rank; null in recipe order. */
  share: number | null
  measured: boolean
  /** An intermediate's own components (Lost Chapter's), in the same terms. */
  components: ItemRouteStep[]
}

/** Where a component stands for the gold in hand. */
export type PathState = 'owned' | 'buy' | 'later'

export interface PathTile {
  itemId: number
  /** What it costs from here — its price less the parts of it already held; 0 when owned. */
  cost: number
  state: PathState
  /**
   * Bought now only if the gold the API floors to 50 hides enough: the sum
   * the walk reached sits inside that step. Said "≈" rather than promised.
   */
  approx: boolean
  /** Share of the mains that bought it at this rank, when measured. */
  share: number | null
}

/** A component of the path: one tile, or — for an intermediate being started — its own parts. */
export interface PathEntry extends PathTile {
  /** The intermediate's parts, in the mains' order, when it is shown open; empty otherwise. */
  parts: PathTile[]
}

export interface BuildPath {
  entries: PathEntry[]
  /** False when the order is the recipe's: too few games, or an API that predates #2183. */
  measured: boolean
}

/** The recipe of an item as steps, in the order Data Dragon lists it — what stands in when nothing was measured. */
function recipeSteps(itemId: number, items: Record<number, StaticItemData>, depth = 0): ItemRouteStep[] {
  if (depth > 3) return []
  return (items[itemId]?.from ?? []).map((part, index) => ({
    itemId: part,
    cost: items[part]?.totalGold ?? 0,
    rank: index + 1,
    share: null,
    measured: false,
    components: recipeSteps(part, items, depth + 1),
  }))
}

/**
 * Takes `itemId` out of the pool when held and returns 0; otherwise returns
 * what it costs from here and takes out the parts of it held — the shop's own
 * arithmetic, as `goldToComplete` does it, but spending the pool so two
 * components never count the same held part.
 */
function spend(itemId: number, pool: number[], items: Record<number, StaticItemData>): number {
  const index = pool.indexOf(itemId)
  if (index !== -1) {
    pool.splice(index, 1)
    return 0
  }
  const item = items[itemId]
  if (!item) return 0
  const parts = item.from ?? []
  const combine = item.totalGold - parts.reduce((sum, part) => sum + (items[part]?.totalGold ?? 0), 0)
  return Math.max(0, combine) + parts.reduce((sum, part) => sum + spend(part, pool, items), 0)
}

/** Whether any part of `itemId`'s recipe, at any depth, is in the pool. */
function holdsPart(itemId: number, pool: number[], items: Record<number, StaticItemData>, depth = 0): boolean {
  if (depth > 3) return false
  return (items[itemId]?.from ?? []).some(part => pool.includes(part) || holdsPart(part, pool, items, depth + 1))
}

/**
 * The build path toward `itemId` (#2183): its components in the order the
 * mains buy them, each owned, bought now or later.
 *
 * The gold in hand walks the ordered list and covers every component it
 * reaches, in order — never the cheapest first: the first one it cannot pay
 * for stops it, and everything after is later. A component that is itself a
 * recipe (Lost Chapter) is one tile, opened into its own parts when some of
 * them are held, or when the gold stops inside it and its first parts can be
 * bought now: the purchase the panel names is always one the shop sells.
 */
export function buildPath(
  itemId: number,
  route: ItemRouteStep[] | undefined,
  held: number[],
  gold: number,
  items: Record<number, StaticItemData>,
): BuildPath {
  const steps = route?.length ? route : recipeSteps(itemId, items)
  const pool = [...held]
  let spent = 0
  let stopped = false

  function place(step: ItemRouteStep): PathTile {
    const cost = spend(step.itemId, pool, items)
    const tile = { itemId: step.itemId, cost, share: step.share }
    if (cost === 0) return { ...tile, state: 'owned', approx: false }
    if (stopped) return { ...tile, state: 'later', approx: false }
    const total = spent + cost
    if (total < gold + GOLD_STEP) {
      spent = total
      return { ...tile, state: 'buy', approx: total > gold }
    }
    stopped = true
    return { ...tile, state: 'later', approx: false }
  }

  /**
   * The intermediate as its own parts, or null when that says nothing the
   * closed tile does not: every part held (only the combine is left), or —
   * opened only because the gold stops inside it — none of them bought now.
   */
  function open(step: ItemRouteStep, recipe: ItemRouteStep[], partly: boolean): PathEntry | null {
    const saved = { pool: [...pool], spent, stopped }
    const cost = spend(step.itemId, [...pool], items)
    const parts = recipe.map(place)
    const buys = parts.some(part => part.state === 'buy')
    if (parts.some(part => part.state !== 'owned') && (partly || buys)) {
      return { itemId: step.itemId, cost, state: buys ? 'buy' : 'later', approx: parts.some(part => part.approx), share: step.share, parts }
    }
    pool.splice(0, pool.length, ...saved.pool)
    spent = saved.spent
    stopped = saved.stopped
    return null
  }

  const entries = steps.map((step): PathEntry => {
    const recipe = step.components.length ? step.components : recipeSteps(step.itemId, items)
    if (!pool.includes(step.itemId) && recipe.length > 0) {
      const partly = holdsPart(step.itemId, pool, items)
      const stopsInside = !stopped && spent + spend(step.itemId, [...pool], items) >= gold + GOLD_STEP
      const opened = partly || stopsInside ? open(step, recipe, partly) : null
      if (opened) return opened
    }
    return { ...place(step), parts: [] }
  })

  return { entries, measured: steps.length > 0 && steps.every(step => step.measured) }
}
