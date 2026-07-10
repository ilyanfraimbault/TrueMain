import type { MatchDetailItemEvent } from '~~/shared/types/match-detail'
import type { StaticItemData } from '~~/shared/types/static-data'

/**
 * Resolve the item an event actually concerns. Riot sets `itemId = 0` on an
 * `ITEM_UNDO` and carries the affected item in `beforeId` / `afterId`, so fall
 * back to those. Returns 0 when nothing resolves.
 */
export function resolveEventItemId(ev: MatchDetailItemEvent): number {
  return ev.itemId || ev.beforeId || ev.afterId || 0
}

/**
 * DDragon flags an item the player can't buy in the shop as
 * `gold.purchasable = false` and/or `inStore = false`. Those items only ever
 * enter a build via an auto-granted transform — a support/role quest upgrade
 * stage, or the empowered-recall boots upgrade — never a shop action. Items
 * absent from the static catalog are treated as shop items: better to render a
 * purchase we lack metadata for than to silently hide a real one.
 */
function isNonShopItem(itemId: number, items: Record<number, StaticItemData>): boolean {
  const item = items[itemId]
  if (!item) return false
  return item.purchasable === false || item.inStore === false
}

/**
 * Whether an item event belongs in a player's build order — the sequence of
 * deliberate shop actions:
 *  - `ITEM_PURCHASED`: kept, unless the item is an auto-transform (non-shop).
 *  - `ITEM_SOLD` / `ITEM_UNDO`: kept — a deliberate divestment / a correction.
 *  - `ITEM_DESTROYED`: dropped — always a *consequence* (a component consumed
 *    into a completed item, or the old item consumed by a transform), never a
 *    shop action. Riot reliably emits it alongside the transform's
 *    `ITEM_PURCHASED`, so keeping it would leave a ghost icon at the transform
 *    minute for the very cases we filter on the purchase side.
 */
export function isBuildOrderEvent(ev: MatchDetailItemEvent, items: Record<number, StaticItemData>): boolean {
  switch (ev.eventType) {
    case 'ITEM_PURCHASED':
      return !isNonShopItem(resolveEventItemId(ev), items)
    case 'ITEM_DESTROYED':
      return false
    default:
      return true
  }
}

/**
 * Minimum pickrate (as a 0–1 ratio) an alternative must reach to be shown in a
 * build "variations" panel. League's long tail is heavily skewed, so anything
 * below this is statistical noise that clutters the panel without informing the
 * choice. The build *tree* is intentionally exempt — it shows the full path.
 */
export const MIN_VARIATION_PICKRATE = 0.05

/**
 * Drop variation options whose pickrate falls below {@link MIN_VARIATION_PICKRATE}.
 * Works on any build option shape (items, spells, skill order, rune pages) since
 * they all expose a `pickRate`.
 */
export function filterByPickRate<T extends { pickRate: number }>(options: T[]): T[] {
  return options.filter(option => option.pickRate >= MIN_VARIATION_PICKRATE)
}
