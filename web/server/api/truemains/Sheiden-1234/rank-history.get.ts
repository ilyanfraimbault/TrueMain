import { createError, defineEventHandler } from 'h3'
import { buildSheidenRankHistoryResponse } from '../../../utils/sheiden-1234-fixture'

// Dev fixture — see profile.get.ts in this folder. Each request rebuilds
// the timeseries with the current `now` so the trailing edge always sits
// at "today" no matter when the user opens the page. Gated to dev (see
// profile.get.ts).
export default defineEventHandler(() => {
  if (!import.meta.dev) {
    throw createError({ statusCode: 404, statusMessage: 'Not Found' })
  }
  return buildSheidenRankHistoryResponse()
})
