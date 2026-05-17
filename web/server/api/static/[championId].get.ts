import { loadStaticData } from '~~/server/utils/ddragon-loader'

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
    getKey: (event) => {
      const championId = getRouterParam(event, 'championId')
      const { patch } = getQuery(event) as { patch?: string }
      return `${championId}-${patch ?? ''}`
    },
  },
)
