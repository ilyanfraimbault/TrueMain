import type { RuneSheet, RuneTreeResponse } from '../types/static-data'

/**
 * The rune tree's sprite sheets (#999): layout and lookup, shared by the server
 * that draws the sheets and the component that cuts icons out of them, so the
 * two cannot disagree on where a perk sits.
 */

/**
 * Edge of one cell, px. The canonical icon fetch size (`ICON_FETCH_SIZE`), so a
 * rune drawn from the sheet has exactly the pixels the same rune fetched alone
 * through `/_ipx` would have.
 */
export const RUNE_SHEET_CELL = 64

/**
 * Transparent px between cells. A cell drawn at a size that is not a whole
 * fraction of 64 px is resampled, and the filter reads a pixel or so past the
 * cell's edge; the gap makes what it reads transparent instead of a neighbour.
 */
export const RUNE_SHEET_GAP = 2

/**
 * The perks each sheet holds, in cell order: one sheet per style (its keystones,
 * then its rows) and one for the stat shards, each perk once. A rune tree draws
 * from three of them — its primary and secondary styles and the shards — so a
 * page pays for the styles it shows, not for all of them. Ids the tree has no
 * icon for are left out; the component draws those the usual way.
 */
export function runeSheetGroups(tree: Pick<RuneTreeResponse, 'styles' | 'perks' | 'shardSlots'>): number[][] {
  const group = (ids: number[]) => [...new Set(ids)].filter(id => Boolean(tree.perks[id]?.iconUrl))
  return [
    ...tree.styles.map(style => group([...style.keystones, ...style.subRows.flat()])),
    group(tree.shardSlots.flat()),
  ].filter(ids => ids.length > 0)
}

/** A near-square grid for `count` cells. */
export function runeSheetGrid(count: number): { columns: number, rows: number } {
  const columns = Math.max(1, Math.ceil(Math.sqrt(count)))
  return { columns, rows: Math.max(1, Math.ceil(count / columns)) }
}

/** Top-left corner of cell `index` on the sheet, px. */
export function runeSheetCellOrigin(
  sheet: Pick<RuneSheet, 'cell' | 'gap' | 'columns'>,
  index: number,
): { x: number, y: number } {
  const pitch = sheet.cell + sheet.gap
  return { x: (index % sheet.columns) * pitch, y: Math.floor(index / sheet.columns) * pitch }
}

/** Full sheet size, px. */
export function runeSheetSize(sheet: Pick<RuneSheet, 'cell' | 'gap' | 'columns' | 'rows'>): { width: number, height: number } {
  const pitch = sheet.cell + sheet.gap
  return { width: sheet.columns * pitch - sheet.gap, height: sheet.rows * pitch - sheet.gap }
}

/** The sheet holding `perkId`, if any. */
export function findRuneSheet(sheets: readonly RuneSheet[] | null | undefined, perkId: number): RuneSheet | null {
  return sheets?.find(sheet => sheet.perkIds.includes(perkId)) ?? null
}

/**
 * CSS that draws `perkId` from the sheet into a `size` px box, or null when the
 * sheet does not hold it. The whole sheet is scaled by `size / cell` and moved
 * so the perk's cell lands on the box.
 */
export function runeSheetIconStyle(
  sheet: RuneSheet,
  perkId: number,
  size: number,
): Record<string, string> | null {
  const index = sheet.perkIds.indexOf(perkId)
  if (index < 0 || size <= 0) return null
  const scale = size / sheet.cell
  const origin = runeSheetCellOrigin(sheet, index)
  const { width, height } = runeSheetSize(sheet)
  const px = (value: number) => `${Number((value * scale).toFixed(3))}px`
  return {
    backgroundImage: `url("${sheet.url}")`,
    backgroundSize: `${px(width)} ${px(height)}`,
    backgroundPosition: `${px(-origin.x)} ${px(-origin.y)}`,
    backgroundRepeat: 'no-repeat',
  }
}
