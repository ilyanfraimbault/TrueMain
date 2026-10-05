import type { ChampionItemContextItem } from '#shared/types/item-context'
import type { BuildCoreView, BuildTreeNode } from '~/types/build'

/**
 * What the item set import sends the shell (`lcu::item_sets::BuildItems`):
 * the build's items by block, in the order the shop shows them. A block left
 * empty is not written — nothing here pads one.
 */
export interface ItemSetBuild {
  starter: number[]
  boots: number[]
  core: number[]
  situational: number[]
}

/**
 * The build's situational items from the item context (#1450): the completed
 * items whose verdict is `Situational`, most picked first, the core path left
 * out. An item judged on several branches appears once, at its best rate.
 */
export function situationalFromContext(items: ChampionItemContextItem[], core: number[]): number[] {
  const best = new Map<number, number>()
  for (const item of items) {
    if (item.slot !== 'Build' || item.class !== 'Situational' || core.includes(item.itemId)) continue
    best.set(item.itemId, Math.max(best.get(item.itemId) ?? 0, item.pickRate))
  }
  return [...best.entries()].sort((a, b) => b[1] - a[1]).map(([itemId]) => itemId)
}

/**
 * The fallback when the slice has no verdicts: what else the build tree went
 * to at each step of the core path — the path's siblings — most played first.
 * `tree` holds what followed the first item, which `path` starts with.
 */
export function situationalFromTree(tree: BuildTreeNode[], path: number[]): number[] {
  const games = new Map<number, number>()
  let level = tree
  for (const step of path.slice(1)) {
    for (const node of level) {
      if (node.itemId !== step && !path.includes(node.itemId)) {
        games.set(node.itemId, (games.get(node.itemId) ?? 0) + node.games)
      }
    }
    const next = level.find(node => node.itemId === step)
    if (!next) break
    level = next.children
  }
  return [...games.entries()].sort((a, b) => b[1] - a[1]).map(([itemId]) => itemId)
}

/**
 * The set's blocks from the build on screen, every id checked against the
 * patch's items: one the game no longer has would sit in the shop as an empty
 * slot.
 */
export function itemSetBuild(core: BuildCoreView, situational: number[], known: (itemId: number) => boolean): ItemSetBuild {
  const keep = (ids: number[] | undefined) => (ids ?? []).filter(known)
  return {
    starter: keep(core.starterItems?.itemIds),
    boots: keep(core.boots?.itemIds),
    core: keep(core.itemPath?.itemIds),
    situational: keep(situational),
  }
}

/** Whether the set would have anything in it. */
export function hasItems(build: ItemSetBuild): boolean {
  return build.starter.length + build.boots.length + build.core.length + build.situational.length > 0
}
