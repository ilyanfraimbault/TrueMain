import { IPX_CACHE_SECONDS } from '~~/shared/utils/ipx'
import { getRuneSheet } from '~~/server/utils/rune-sheet-render'
import { loadRuneTree, normalizeCdragonPatch, RUNE_SHEET_ROUTE_BASE } from '~~/server/utils/rune-tree'

/**
 * `/_rune-sheet/<patch|latest>/<hash>.webp` — one rune style's perk icons (or the
 * stat shards) on one image (#999), at a URL `/api/static/rune-tree` hands out in
 * `sheets`.
 *
 * Only the sheets of the tree the server currently has are ever drawn: the URL
 * must be exactly one that tree describes, so this public route cannot be made
 * to draw or store anything else. A URL that no longer matches (the tree
 * changed under a long-lived page) answers 404, and the page then draws its
 * runes icon by icon.
 */
export default defineEventHandler(async (event) => {
  const patchParam = getRouterParam(event, 'patch') ?? ''
  const file = getRouterParam(event, 'file') ?? ''
  const patch = patchParam === 'latest' ? null : normalizeCdragonPatch(patchParam)

  // Errors must not inherit a long browser cache: the rune would stay drawn
  // icon by icon for a week after a transient upstream failure.
  setResponseHeader(event, 'cache-control', 'no-store')

  if (patch !== null && patch !== patchParam) {
    throw createError({ statusCode: 404, statusMessage: 'Unknown rune sheet' })
  }

  const tree = await loadRuneTree(event, patch)
  const url = `${RUNE_SHEET_ROUTE_BASE}/${patchParam}/${file}`
  const sheet = tree.sheets?.find(candidate => candidate.url === url)
  if (!sheet) {
    throw createError({ statusCode: 404, statusMessage: 'Unknown rune sheet' })
  }

  let body: Awaited<ReturnType<typeof getRuneSheet>>
  try {
    body = await getRuneSheet(tree, sheet)
  }
  catch (error) {
    throw createError({ statusCode: 502, statusMessage: 'Rune sheet could not be drawn', cause: error })
  }

  setResponseHeader(event, 'content-type', 'image/webp')
  setResponseHeader(event, 'cache-control', `public, max-age=${IPX_CACHE_SECONDS}, immutable`)
  setResponseHeader(event, 'content-security-policy', "default-src 'none'")
  return body
})
