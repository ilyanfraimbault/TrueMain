import { createError, defineEventHandler } from 'h3'
import { SHEIDEN_EMPTY_MATCHES } from '../../../utils/sheiden-1234-fixture'

// Dev fixture — see profile.get.ts in this folder. Returns an empty match
// feed so the right rail shows MatchHistoryEmpty rather than spamming the
// API proxy with an unreachable backend. Gated to dev (see profile.get.ts).
export default defineEventHandler(() => {
  if (!import.meta.dev) {
    throw createError({ statusCode: 404, statusMessage: 'Not Found' })
  }
  return SHEIDEN_EMPTY_MATCHES
})
