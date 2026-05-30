import { createError, defineEventHandler } from 'h3'
import { SHEIDEN_PROFILE } from '../../../utils/sheiden-1234-fixture'

// Dev fixture — short-circuits the API proxy for `/truemains/Sheiden-1234`
// so the unified ranked card can be inspected without a running backend.
// See server/utils/sheiden-1234-fixture.ts for the data. Gated to dev so the
// fixture never surfaces in a deployed (production / QA) build.
export default defineEventHandler(() => {
  if (!import.meta.dev) {
    throw createError({ statusCode: 404, statusMessage: 'Not Found' })
  }
  return SHEIDEN_PROFILE
})
