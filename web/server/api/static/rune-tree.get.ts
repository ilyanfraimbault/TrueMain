import type { RuneTreeResponse } from '~~/shared/types/static-data'
import { loadRuneTree, normalizeCdragonPatch } from '~~/server/utils/rune-tree'

export default defineEventHandler(async (event): Promise<RuneTreeResponse> => {
  const { patch } = getQuery(event)
  const normalized = normalizeCdragonPatch(typeof patch === 'string' ? patch : null)
  return loadRuneTree(event, normalized)
})
