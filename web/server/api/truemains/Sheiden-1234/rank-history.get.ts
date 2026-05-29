import { defineEventHandler } from 'h3'
import { buildSheidenRankHistoryResponse } from '../../../utils/sheiden-1234-fixture'

// Dev fixture — see profile.get.ts in this folder. Each request rebuilds
// the timeseries with the current `now` so the trailing edge always sits
// at "today" no matter when the user opens the page.
export default defineEventHandler(() => buildSheidenRankHistoryResponse())
