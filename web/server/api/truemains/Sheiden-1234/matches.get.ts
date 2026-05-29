import { defineEventHandler } from 'h3'
import { SHEIDEN_EMPTY_MATCHES } from '../../../utils/sheiden-1234-fixture'

// Dev fixture — see profile.get.ts in this folder. Returns an empty match
// feed so the right rail shows MatchHistoryEmpty rather than spamming the
// API proxy with an unreachable backend.
export default defineEventHandler(() => SHEIDEN_EMPTY_MATCHES)
