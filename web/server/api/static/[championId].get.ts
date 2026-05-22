import { loadStaticData } from '~~/server/utils/ddragon-loader'
import { normalizeDataDragonPatch } from '~~/shared/utils/ddragon'

export default defineCachedEventHandler(
  async (event) => {
    const championId = Number(getRouterParam(event, 'championId'))
    if (!Number.isFinite(championId)) {
      throw createError({ statusCode: 400, statusMessage: 'Invalid championId' })
    }
    const { patch } = getQuery(event) as { patch?: string }
    return loadStaticData(championId, patch ?? null)
  },
  {
    maxAge: 60 * 60,
    name: 'ddragon-static',
    // Normalize the patch the same way `loadStaticData` does so `?patch=16.5`
    // and `?patch=16.5.1` share a cache entry instead of fragmenting into two
    // semantically identical ones.
    getKey: (event) => {
      const championId = getRouterParam(event, 'championId')
      const { patch } = getQuery(event) as { patch?: string }
      return `${championId}-${normalizeDataDragonPatch(patch) ?? ''}`
    },
  },
)
