import { defineEventHandler } from 'h3'

/**
 * The champion list endpoint (`/api/static/champions`) comes from the layer this
 * app shares with the public site (`web/layers/nitro`, #1625), where it is open.
 * Here it sits behind the operator session like every other route, so the gate
 * lives beside the app rather than in the shared handler. Matched on a path
 * boundary, the same prefix `app/utils/session-expiry.ts` reads a 401 from.
 */
export default defineEventHandler(async (event) => {
  const [pathname = ''] = event.path.split('?', 1)
  if (pathname === '/api/static' || pathname.startsWith('/api/static/')) {
    await requireUserSession(event)
  }
})
