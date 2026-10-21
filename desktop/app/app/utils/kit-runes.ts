import type { KitRunePage } from '#shared/types/kit'
import type { RuneTreeResponse, RuneTreeStyle } from '#shared/types/static-data'

/**
 * A kit's rune page (#2185), edited the way the client edits one: a tree
 * changed resets its choices to each row's first rune, a rune clicked takes
 * its row, and the secondary tree's two runes always sit on two different
 * rows. Every edit returns a new page.
 */

const styleOf = (tree: RuneTreeResponse, styleId: number): RuneTreeStyle | null =>
  tree.styles.find(style => style.styleId === styleId) ?? null

/** The row of `style`'s sub-rows that holds `perkId`, or -1. */
const rowOf = (style: RuneTreeStyle, perkId: number) => style.subRows.findIndex(row => row.includes(perkId))

/** The first rune of each of the secondary tree's first two rows. */
function secondaryDefaults(style: RuneTreeStyle): Pick<KitRunePage, 'secondaryPerk1Id' | 'secondaryPerk2Id'> {
  return { secondaryPerk1Id: style.subRows[0]?.[0] ?? 0, secondaryPerk2Id: style.subRows[1]?.[0] ?? 0 }
}

/** A complete page of each row's first rune: what a kit starts from when its build carried none. */
export function defaultRunePage(tree: RuneTreeResponse): KitRunePage | null {
  const [primary, secondary] = tree.styles
  if (!primary || !secondary) return null
  return {
    ...withPrimaryStyle({ secondaryStyleId: secondary.styleId, ...secondaryDefaults(secondary) } as KitRunePage, tree, primary.styleId),
    statOffense: tree.shardSlots[0]?.[0] ?? 0,
    statFlex: tree.shardSlots[1]?.[0] ?? 0,
    statDefense: tree.shardSlots[2]?.[0] ?? 0,
  }
}

/** The page on another primary tree: its first keystone and first runes; a secondary tree it takes moves aside. */
export function withPrimaryStyle(page: KitRunePage, tree: RuneTreeResponse, styleId: number): KitRunePage {
  const style = styleOf(tree, styleId)
  if (!style) return page
  const next: KitRunePage = {
    ...page,
    primaryStyleId: styleId,
    primaryKeystoneId: style.keystones[0] ?? 0,
    primaryPerk1Id: style.subRows[0]?.[0] ?? 0,
    primaryPerk2Id: style.subRows[1]?.[0] ?? 0,
    primaryPerk3Id: style.subRows[2]?.[0] ?? 0,
  }
  if (page.secondaryStyleId !== styleId) return next
  const other = tree.styles.find(candidate => candidate.styleId !== styleId)
  return other ? { ...next, secondaryStyleId: other.styleId, ...secondaryDefaults(other) } : next
}

/** The page on another secondary tree, which cannot be the primary one. */
export function withSecondaryStyle(page: KitRunePage, tree: RuneTreeResponse, styleId: number): KitRunePage {
  const style = styleOf(tree, styleId)
  if (!style || styleId === page.primaryStyleId) return page
  return { ...page, secondaryStyleId: styleId, ...secondaryDefaults(style) }
}

/** A primary rune clicked: the keystone, or the rune of its row. */
export function withPrimaryPerk(page: KitRunePage, tree: RuneTreeResponse, perkId: number): KitRunePage {
  const style = styleOf(tree, page.primaryStyleId)
  if (!style) return page
  if (style.keystones.includes(perkId)) return { ...page, primaryKeystoneId: perkId }
  switch (rowOf(style, perkId)) {
    case 0: return { ...page, primaryPerk1Id: perkId }
    case 1: return { ...page, primaryPerk2Id: perkId }
    case 2: return { ...page, primaryPerk3Id: perkId }
    default: return page
  }
}

/**
 * A secondary rune clicked: it replaces the chosen rune of its own row when
 * there is one, else the older of the two — the client's rule — and the two
 * are kept in row order.
 */
export function withSecondaryPerk(page: KitRunePage, tree: RuneTreeResponse, perkId: number): KitRunePage {
  const style = styleOf(tree, page.secondaryStyleId)
  if (!style) return page
  const row = rowOf(style, perkId)
  if (row === -1) return page
  let first = page.secondaryPerk1Id
  let second = page.secondaryPerk2Id
  if (rowOf(style, first) === row) first = perkId
  else if (rowOf(style, second) === row) second = perkId
  else [first, second] = [second, perkId]
  const ordered = rowOf(style, first) <= rowOf(style, second) ? [first, second] : [second, first]
  return { ...page, secondaryPerk1Id: ordered[0]!, secondaryPerk2Id: ordered[1]! }
}

const SHARD_FIELDS = ['statOffense', 'statFlex', 'statDefense'] as const

/** A shard clicked on row `row` (offense, flex, defense). */
export function withShard(page: KitRunePage, tree: RuneTreeResponse, row: number, perkId: number): KitRunePage {
  const field = SHARD_FIELDS[row]
  if (!field || !tree.shardSlots[row]?.includes(perkId)) return page
  return { ...page, [field]: perkId }
}

/** What is wrong with a page against this patch's trees, in the player's words; empty when it is complete. */
export function runeIssues(page: KitRunePage, tree: RuneTreeResponse): string[] {
  const primary = styleOf(tree, page.primaryStyleId)
  const secondary = styleOf(tree, page.secondaryStyleId)
  const issues: string[] = []
  if (!primary) return ['Choose a primary rune tree.']
  if (!primary.keystones.includes(page.primaryKeystoneId)) issues.push('The keystone must belong to the primary tree.')
  const primaryPerks = [page.primaryPerk1Id, page.primaryPerk2Id, page.primaryPerk3Id]
  if (primaryPerks.some((perkId, row) => !primary.subRows[row]?.includes(perkId))) issues.push('Choose one rune on each row of the primary tree.')
  if (!secondary || secondary.styleId === primary.styleId) {
    issues.push('Choose a secondary tree other than the primary one.')
  }
  else {
    const rows = [page.secondaryPerk1Id, page.secondaryPerk2Id].map(perkId => rowOf(secondary, perkId))
    if (rows.includes(-1) || rows[0] === rows[1]) issues.push('Choose two secondary runes on two different rows.')
  }
  const shards = [page.statOffense, page.statFlex, page.statDefense]
  if (shards.some((perkId, row) => !tree.shardSlots[row]?.includes(perkId))) issues.push('Choose a shard on each row.')
  return issues
}
