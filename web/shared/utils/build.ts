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
 * Pair each id of a build dimension with whatever the item map can resolve for
 * it *right now*, keeping the ids it cannot resolve as `null` rather than
 * dropping them.
 *
 * Every consumer renders one icon slot per id, and the item map is the
 * deferred, patch-pinned, ~370 KiB static fetch — it lands well after the build
 * payload carrying these ids. Filtering the unresolved ids out collapsed the
 * slot count to zero for that whole window, so a build that *did* have boots
 * and a starter set rendered its own "No data" state until the map arrived.
 * Keeping the nulls means the row holds its shape, each slot shows the loading
 * skeleton `GameTooltipItemIcon` already renders for a null item, and the
 * no-data question is settled by the ids alone — which is the only thing that
 * actually answers it.
 */
export function itemSlots(
  ids: number[] | null | undefined,
  itemsMap: Record<number, StaticItemData>,
): { id: number, item: StaticItemData | null }[] {
  return (ids ?? []).map(id => ({ id, item: itemsMap[id] ?? null }))
}

/**
 * Map-objective pickups that occupy a real inventory/trinket slot in Riot's
 * data but are not part of a player's build — the Eye of the Herald replaces
 * the trinket slot while a player carries the Rift Herald summon. They must
 * never show up in a final-build item display. Ornn masterwork upgrades are
 * deliberately NOT here: those are real completed items despite being
 * non-purchasable, so a generic `purchasable === false` filter would wrongly
 * hide them.
 */
const NON_BUILD_ITEM_IDS = new Set<number>([
  3513, // Eye of the Herald (Rift Herald summon)
])

/** True for map-objective pickups that shouldn't appear in a final build. */
export function isNonBuildItem(itemId: number): boolean {
  return NON_BUILD_ITEM_IDS.has(itemId)
}

/** True when the item is a pair of boots (DDragon `tags` contains "Boots"). */
export function isBootsItem(item: StaticItemData | null | undefined): boolean {
  return item?.tags?.includes('Boots') ?? false
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
export const MIN_VARIATION_PICKRATE = 0.1

/**
 * Most alternatives a variation panel shows for one category. Past the third the
 * options are no longer a decision the reader makes, they are the shape of the
 * long tail — which the build tree already draws.
 */
export const MAX_VARIATION_OPTIONS = 3

/**
 * Drop variation options whose pickrate falls below {@link MIN_VARIATION_PICKRATE}.
 * Works on any build option shape (items, spells, skill order, rune pages) since
 * they all expose a `pickRate`.
 */
export function filterByPickRate<T extends { pickRate: number }>(options: T[]): T[] {
  return options.filter(option => option.pickRate >= MIN_VARIATION_PICKRATE)
}

/**
 * The options a variation panel should render for one category, or none at all
 * (#1466).
 *
 * A category with a single surviving option is not a choice: the core block
 * above already shows that option, so the card would restate it under a heading
 * promising alternatives — which is exactly what a 100%-pickrate "Summoner
 * spells" card did. Returning an empty list lets the caller drop the whole card
 * and, when every category is settled, the whole section: a champion whose build
 * is not up for debate should say so by being short, not by listing one row four
 * times.
 *
 * Sorted before capping rather than trusting serialization order, so the three
 * that survive are the three most played.
 */
export function variationOptions<T extends { pickRate: number }>(options: T[]): T[] {
  const kept = filterByPickRate(options)
    .slice()
    .sort((a, b) => b.pickRate - a.pickRate)
    .slice(0, MAX_VARIATION_OPTIONS)
  return kept.length > 1 ? kept : []
}
