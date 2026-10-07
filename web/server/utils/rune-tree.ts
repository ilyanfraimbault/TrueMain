import { createHash } from 'node:crypto'
import type { RuneSheet, RuneTreeResponse, RuneTreeStyle } from '~~/shared/types/static-data'
import {
  RUNE_SHEET_CELL,
  RUNE_SHEET_GAP,
  runeSheetGrid,
  runeSheetGroups,
} from '~~/shared/utils/rune-sheet'
import {
  buildPerkMap,
  buildPerkStyleMap,
  communityDragonPrefix,
  rewriteCdragonAsset,
  type CdragonPerkRow,
  type CdragonPerkStyleRow,
} from '~~/server/utils/ddragon-loader'

interface PerkStylesResponse {
  styles: Array<CdragonPerkStyleRow & {
    slots: Array<{ type: string, perks: number[] }>
  }>
}

/** Route the rune sheets are served from: `/_rune-sheet/<patch|latest>/<hash>.webp`. */
export const RUNE_SHEET_ROUTE_BASE = '/_rune-sheet'

/**
 * Bumped when the way the sheet is drawn changes (cell size, resampling, format),
 * so every sheet URL changes with it and no browser keeps the old drawing.
 */
const RUNE_SHEET_RENDER_VERSION = 1

// Normalize patches into the CDragon-friendly `major.minor` form (DDragon
// patches like `15.10.1` reduce to `15.10`). Anything that doesn't match the
// strict numeric format is rejected so a hostile `?patch=` can't traverse out
// of the CDragon prefix; the request falls back to `latest` downstream.
const PATCH_FORMAT_RE = /^\d+\.\d+(?:\.\d+)?$/
export function normalizeCdragonPatch(patch: string | null | undefined): string | null {
  if (!patch || !PATCH_FORMAT_RE.test(patch)) return null
  const segments = patch.split('.')
  return `${segments[0]}.${segments[1]}`
}

async function fetchRuneTree(patch: string | null): Promise<RuneTreeResponse> {
  const prefix = communityDragonPrefix(patch)

  const [perks, perkStyles] = await Promise.all([
    $fetch<CdragonPerkRow[]>(`${prefix}/v1/perks.json`),
    $fetch<PerkStylesResponse>(`${prefix}/v1/perkstyles.json`),
  ])

  const perkMap = buildPerkMap(perks, patch)
  const perkStyleMap = buildPerkStyleMap(perkStyles.styles, patch)

  // CommunityDragon emits 7 slots per style: 0 = keystone, 1-3 = regular
  // sub-rows, 4-6 = stat shards (same triplets for every style, so we read
  // them once from any style).
  const styles: RuneTreeStyle[] = perkStyles.styles.map(style => ({
    styleId: style.id,
    name: style.name,
    iconUrl: rewriteCdragonAsset(style.iconPath, patch),
    keystones: style.slots[0]?.perks ?? [],
    subRows: style.slots.slice(1, 4).map(slot => slot.perks),
  }))

  const firstStyle = perkStyles.styles[0]
  const shardSlots = firstStyle ? firstStyle.slots.slice(4, 7).map(slot => slot.perks) : []

  return {
    styles,
    perks: perkMap,
    perkStyles: perkStyleMap,
    shardSlots,
  }
}

/**
 * The sheets a tree is drawn on. Each URL carries a hash of everything its
 * drawing depends on — which perks, in which cells, from which icon URLs — so it
 * is immutable: a tree that changes (CommunityDragon publishing the pinned patch
 * after a `latest` fallback, say) gets new URLs rather than stale cached sheets.
 */
export function describeRuneSheets(tree: RuneTreeResponse, patch: string | null): RuneSheet[] {
  return runeSheetGroups(tree).map((perkIds) => {
    const { columns, rows } = runeSheetGrid(perkIds.length)
    const hash = createHash('sha256')
      .update(JSON.stringify({
        version: RUNE_SHEET_RENDER_VERSION,
        cell: RUNE_SHEET_CELL,
        gap: RUNE_SHEET_GAP,
        columns,
        icons: perkIds.map(id => [id, tree.perks[id]?.iconUrl]),
      }))
      .digest('hex')
      .slice(0, 16)

    return {
      url: `${RUNE_SHEET_ROUTE_BASE}/${patch ?? 'latest'}/${hash}.webp`,
      cell: RUNE_SHEET_CELL,
      gap: RUNE_SHEET_GAP,
      columns,
      rows,
      perkIds,
    }
  })
}

async function fetchRuneTreeWithFallback(patch: string | null): Promise<RuneTreeResponse> {
  // CommunityDragon publishes the per-patch directory hours-to-days after
  // DDragon lists the version in versions.json, so early in a patch cycle
  // the pinned URL 404s while `latest` already serves the new data. Fall
  // back to `latest` (icon URLs included) rather than 502ing — a 502 here
  // starves `staticBundleReady` on every profile page. The fallback payload
  // does get cached under the patch key for 1h, which is fine: rune data
  // barely changes between patches and the next miss retries the pinned URL.
  try {
    return await fetchRuneTree(patch)
  }
  catch (patchError) {
    // On errors with `latest` itself, bubble up as 502 — we'd rather fail
    // once and let the next request retry than cache an empty
    // `RuneTreeResponse` for 1h on a transient outage. Same trade-off as
    // champions.get.ts.
    if (patch === null) {
      throw createError({
        statusCode: 502,
        statusMessage: 'CommunityDragon rune tree fetch failed',
        cause: patchError,
      })
    }
    return fetchRuneTree(null).catch((error) => {
      throw createError({
        statusCode: 502,
        statusMessage: 'CommunityDragon rune tree fetch failed',
        cause: error,
      })
    })
  }
}

/**
 * The rune tree for a CDragon `major.minor` patch (`null` = `latest`), sheets
 * included. Shared by `/api/static/rune-tree` and the sheet route, which must
 * see the very same tree to draw the sheets the response describes.
 */
export const loadRuneTree = defineCachedFunction(
  async (_event, patch: string | null): Promise<RuneTreeResponse> => {
    const tree = await fetchRuneTreeWithFallback(patch)
    return { ...tree, sheets: describeRuneSheets(tree, patch) }
  },
  {
    maxAge: 60 * 60,
    name: 'cdragon-rune-tree',
    // Cache key includes the patch so two pages on different patches don't
    // step on each other's cached payload. `latest` keeps the legacy key.
    getKey: (_event, patch: string | null) => `rune-tree:${patch ?? 'latest'}`,
  },
)
