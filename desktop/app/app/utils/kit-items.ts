import type { StaticItemData } from '#shared/types/static-data'

/**
 * What a kit can hold (#2185): the patch's Summoner's Rift shop, grouped the
 * way the item picker lists it, and the spells a ranked game offers.
 */

export type KitItemGroup = 'legendary' | 'boots' | 'component' | 'starter'

export const KIT_ITEM_GROUPS: { group: KitItemGroup, label: string }[] = [
  { group: 'legendary', label: 'Legendary' },
  { group: 'boots', label: 'Boots' },
  { group: 'component', label: 'Components' },
  { group: 'starter', label: 'Starter' },
]

/** Summoner's Rift's summoner spells (Data Dragon's keys): Cleanse, Exhaust, Flash, Ghost, Heal, Smite, Teleport, Ignite, Barrier. */
export const RIFT_SPELLS = [1, 3, 4, 6, 7, 11, 12, 14, 21]

/** Flash and Ignite: the pair a kit made out of nothing starts on. */
export const DEFAULT_SPELLS: [number, number] = [4, 14]

/**
 * Item ids past this are other modes' (Arena, the mode-specific copies of Rift
 * items); Data Dragon's item list carries every mode.
 */
const RIFT_ID_CEILING = 10_000

/** Below this a completed item is a starter rather than a legendary (Doran's, the support item, Dark Seal). */
const STARTER_GOLD_CEILING = 1_000

const RIFT = 11

/**
 * The shop's items, each in its group: boots by their tag (the plain Boots
 * are a component), a component when another item is built from it, a
 * starter when it is a cheap finished one, a legendary otherwise. Left out:
 * potions, wards, trinkets, what the Rift's shop does not sell, the boots'
 * upgrades (earned in game, never bought), and a second item of one name —
 * Data Dragon lists some twice, the lower id is the Rift's.
 */
export function kitItemGroups(items: Record<number, StaticItemData>): Map<number, KitItemGroup> {
  const isBoots = (id: number) => items[id]?.tags?.includes('Boots') ?? false
  /** Finished boots: boots with a recipe, which only an upgrade is built from. */
  const finishedBoots = (id: number) => isBoots(id) && (items[id]?.from?.length ?? 0) > 0
  const sold = (item: StaticItemData) =>
    item.id < RIFT_ID_CEILING
    && item.totalGold > 0
    && item.purchasable !== false
    && item.inStore !== false
    && (item.maps?.includes(RIFT) ?? true)
    && !item.tags?.some(tag => tag === 'Consumable' || tag === 'Trinket')
    && !(item.from ?? []).some(finishedBoots)
  const names = new Set<string>()
  const shop: StaticItemData[] = []
  for (const item of Object.values(items).sort((a, b) => a.id - b.id)) {
    if (!sold(item) || names.has(item.name)) continue
    names.add(item.name)
    shop.push(item)
  }
  const components = new Set(shop.flatMap(item => item.from ?? []))
  const groups = new Map<number, KitItemGroup>()
  for (const item of shop) {
    if (finishedBoots(item.id)) groups.set(item.id, 'boots')
    else if (components.has(item.id)) groups.set(item.id, 'component')
    else if (item.totalGold < STARTER_GOLD_CEILING) groups.set(item.id, 'starter')
    else groups.set(item.id, 'legendary')
  }
  return groups
}

/**
 * The items of `groups` matching `query` (any word of the name, case and accents
 * ignored), cheapest first in each group, the groups in picker order.
 */
export function searchKitItems(
  items: Record<number, StaticItemData>,
  groups: Map<number, KitItemGroup>,
  query: string,
  only: KitItemGroup[],
): { group: KitItemGroup, label: string, items: StaticItemData[] }[] {
  const fold = (text: string) => text.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase()
  const words = fold(query).split(/\s+/).filter(Boolean)
  const matches = (item: StaticItemData) => words.every(word => fold(item.name).includes(word))
  return KIT_ITEM_GROUPS
    .filter(({ group }) => only.includes(group))
    .map(({ group, label }) => ({
      group,
      label,
      items: [...groups]
        .filter(([, g]) => g === group)
        .map(([id]) => items[id]!)
        .filter(matches)
        .sort((a, b) => a.totalGold - b.totalGold || a.name.localeCompare(b.name)),
    }))
    .filter(section => section.items.length > 0)
}
