import type { EventHandler, H3Event } from 'h3'
import { beforeEach, describe, expect, it, vi } from 'vitest'

// The champion list handler comes from `web/layers/nitro`, where it is open; this app's
// middleware is all that keeps it behind the operator session. `requireUserSession` is a
// nuxt-auth-utils auto-import, stubbed as a global so the middleware runs outside Nitro.
const requireUserSession = vi.fn()
vi.stubGlobal('requireUserSession', (event: H3Event) => requireUserSession(event))

async function loadMiddleware(): Promise<EventHandler> {
  const module = await import('~~/server/middleware/static-session')
  return module.default as EventHandler
}

function eventAt(path: string): H3Event {
  return { path } as H3Event
}

describe('static session middleware', () => {
  beforeEach(() => {
    requireUserSession.mockReset()
    requireUserSession.mockRejectedValue(Object.assign(new Error('Unauthorized'), { statusCode: 401 }))
  })

  it.each([
    '/api/static/champions',
    '/api/static/champions?patch=16.5',
    '/api/static',
  ])('requires the session on %s', async (path) => {
    const middleware = await loadMiddleware()

    await expect(middleware(eventAt(path))).rejects.toThrow('Unauthorized')
    expect(requireUserSession).toHaveBeenCalledTimes(1)
  })

  it('lets a request with a session through', async () => {
    const middleware = await loadMiddleware()
    requireUserSession.mockResolvedValue({ user: { name: 'operator' } })

    await expect(middleware(eventAt('/api/static/champions'))).resolves.toBeUndefined()
  })

  it.each([
    '/api/auth/login',
    '/api/staticky',
    '/login',
  ])('leaves %s to its own handler', async (path) => {
    const middleware = await loadMiddleware()

    await middleware(eventAt(path))

    // The login endpoint in particular must stay reachable without a session.
    expect(requireUserSession).not.toHaveBeenCalled()
  })
})
