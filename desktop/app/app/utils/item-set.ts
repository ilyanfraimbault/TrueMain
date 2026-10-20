import type { ChampionItemContextItem } from '#shared/types/item-context'
import type { Kit } from '#shared/types/kit'
import type { BuildCoreView, BuildTreeNode } from '~/types/build'
import { kitPaths } from '~/utils/kit-tree'

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

/** One titled block of a kit's set (`lcu::item_sets::NamedBlock`). */
export interface NamedItemBlock {
  title: string
  items: number[]
}

/**
 * A kit's set (#2185): the starter and the boots first, then one block per
 * path of its tree, the default path first and titled "Core" when it is the
 * only one; with branches each block is named by the item it ends on. Ids
 * the patch does not know are dropped, as for a build's set.
 */
export function kitItemSetBlocks(kit: Kit, name: (itemId: number) => string, known: (itemId: number) => boolean): NamedItemBlock[] {
  const keep = (ids: number[]) => ids.filter(known)
  const paths = kitPaths(kit.tree).map(keep).filter(path => path.length > 0)
  return [
    { title: 'Starter', items: keep(kit.starter) },
    { title: 'Boots', items: keep(kit.boots ? [kit.boots] : []) },
    ...paths.map((path, index) => ({
      title: paths.length === 1 ? 'Core' : `${index === 0 ? 'Core' : `Path ${index + 1}`} · ${name(path[path.length - 1]!)}`,
      items: path,
    })),
  ].filter(block => block.items.length > 0)
}
