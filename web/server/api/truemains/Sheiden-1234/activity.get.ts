import { createError, defineEventHandler } from 'h3'
import { buildSheidenActivityResponse } from '../../../utils/sheiden-1234-fixture'

// Dev fixture — see profile.get.ts in this folder. Rebuilt per request so the
// trailing edge of the grid always sits at "today", and gated to dev the same
// way its three siblings are.
export default defineEventHandler(() => {
  if (!import.meta.dev) {
    throw createError({ statusCode: 404, statusMessage: 'Not Found' })
  }
  return buildSheidenActivityResponse()
})
