import { Buffer } from 'node:buffer'
import sharp from 'sharp'
import type { RuneSheet, RuneTreeResponse } from '~~/shared/types/static-data'
import { runeSheetCellOrigin, runeSheetSize } from '~~/shared/utils/rune-sheet'
import { createBoundedByteCache } from './bounded-byte-cache'
import { createPatchRetention, isOutsideRetention } from './ipx-patch-retention'

/**
 * Draws a rune tree's sprite sheets (#999) and keeps them in memory.
 *
 * Community Dragon ships no sprite for perks, so the sheet is drawn here, on the
 * first request for it — never at boot: a pre-warm against the volunteer-run
 * mirror was rejected in #997. A sheet costs one fetch per perk icon it holds
 * (~13 for a style), once per patch per process, which is what the individual
 * `/_ipx` icons it replaces cost the first time too.
 *
 * Kept like the `/_ipx` bytes: a byte-bounded LRU, swept by patch with the same
 * retention (the current patch and the two before it). The key carries one of
 * the sheet's icon URLs, which is what tells the retention its patch; a
 * `latest` sheet has none and is left to the LRU, like `latest` icons are.
 */

const cache = createBoundedByteCache<{ body: Buffer, byteLength: number }>({
  // A sheet is ~10–30 KB and a patch has six: the three patches of retention
  // plus `latest` hold well under a megabyte.
  maxBytes: 4 * 1024 * 1024,
  maxEntryBytes: 1024 * 1024,
})
const patchRetention = createPatchRetention()
const inFlight = new Map<string, Promise<Buffer>>()

/** Icons fetched at once while drawing a sheet: polite to the mirror, still quick. */
const FETCH_CONCURRENCY = 6

async function mapLimit<T, R>(items: T[], limit: number, fn: (item: T) => Promise<R>): Promise<R[]> {
  const results: R[] = Array.from({ length: items.length })
  let next = 0
  async function worker() {
    while (next < items.length) {
      const index = next++
      results[index] = await fn(items[index] as T)
    }
  }
  await Promise.all(Array.from({ length: Math.min(limit, items.length) }, worker))
  return results
}

async function drawSheet(tree: RuneTreeResponse, sheet: RuneSheet): Promise<Buffer> {
  const icons = await mapLimit(sheet.perkIds, FETCH_CONCURRENCY, async (id) => {
    const url = tree.perks[id]?.iconUrl
    if (!url) throw new Error(`perk ${id} has no icon`)
    const bytes = await $fetch<ArrayBuffer>(url, { responseType: 'arrayBuffer', timeout: 15_000, retry: 1 })
    // Every cell is filled edge to edge, enlarging the few icons published
    // smaller than a cell — what the browser did to them as single `<img>`s.
    return sharp(Buffer.from(bytes))
      .resize(sheet.cell, sheet.cell, { fit: 'contain', background: { r: 0, g: 0, b: 0, alpha: 0 } })
      .png()
      .toBuffer()
  })

  const { width, height } = runeSheetSize(sheet)
  return sharp({ create: { width, height, channels: 4, background: { r: 0, g: 0, b: 0, alpha: 0 } } })
    .composite(icons.map((input, index) => {
      const { x, y } = runeSheetCellOrigin(sheet, index)
      return { input, left: x, top: y }
    }))
    // IPX's own WebP settings (sharp's default quality), so a rune looks the
    // same cut from the sheet as it did fetched alone.
    .webp()
    .toBuffer()
}

/** The sheet's bytes, drawn on the first call and served from memory after. */
export async function getRuneSheet(tree: RuneTreeResponse, sheet: RuneSheet): Promise<Buffer> {
  const firstIcon = tree.perks[sheet.perkIds[0] ?? -1]?.iconUrl ?? ''
  const key = `${sheet.url} ${firstIcon}`

  const cached = cache.get(key)
  if (cached) return cached.body

  const pending = inFlight.get(key)
  if (pending) return pending

  const drawing = drawSheet(tree, sheet)
    .then((body) => {
      const retained = patchRetention.observe(key)
      if (retained) cache.purge(cacheKey => isOutsideRetention(cacheKey, retained))
      cache.set(key, { body, byteLength: body.byteLength })
      return body
    })
    .finally(() => inFlight.delete(key))
  inFlight.set(key, drawing)
  return drawing
}
