import { defineEventHandler } from 'h3'
import { SHEIDEN_PROFILE } from '../../../utils/sheiden-1234-fixture'

// Dev fixture — short-circuits the API proxy for `/truemains/Sheiden-1234`
// so the unified ranked card can be inspected without a running backend.
// See server/utils/sheiden-1234-fixture.ts for the data.
export default defineEventHandler(() => SHEIDEN_PROFILE)
