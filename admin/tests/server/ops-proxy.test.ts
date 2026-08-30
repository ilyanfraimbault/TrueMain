import type { EventHandler, H3Event } from 'h3'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { isUnsafeProxyPath } from '~~/server/utils/proxy-path'

// The only entry point to the privileged backend. It is a Nitro route, so the three things
// it leans on — `requireUserSession`, `useRuntimeConfig` and `isUnsafeProxyPath` — are
// auto-imports rather than module imports; stubbing them as globals is what lets the
// handler run outside Nitro. `isUnsafeProxyPath` is stubbed with the real implementation
// (imported directly, not mocked) rather than a `vi.fn()`, so the escaping-path cases below
// exercise the actual predicate — pinned separately in tests/proxy-path/proxy-path.test.ts —
// instead of a canned return value. `proxyRequest` is mocked so nothing here ever opens a
// socket, which is also what makes "no session rejects before any network call" an
// assertion rather than a hope.
const proxyRequest = vi.fn()

vi.mock('h3', async (importOriginal) => {
  const actual = await importOriginal<typeof import('h3')>()
  return { ...actual, proxyRequest: (...args: unknown[]) => proxyRequest(...args) }
})

const OPS_KEY = 'super-secret-ops-key-never-sent-to-the-browser'
const BASE = 'https://backend.internal:8080'

const requireUserSession = vi.fn()
const useRuntimeConfig = vi.fn()

vi.stubGlobal('requireUserSession', (event: H3Event) => requireUserSession(event))
vi.stubGlobal('useRuntimeConfig', (event: H3Event) => useRuntimeConfig(event))
vi.stubGlobal('isUnsafeProxyPath', isUnsafeProxyPath)

async function loadHandler(): Promise<EventHandler> {
  const module = await import('~~/server/api/ops/[...path]')
  return module.default as EventHandler
}

function eventAt(path: string): H3Event {
  return { path } as H3Event
}

describe('ops proxy handler', () => {
  beforeEach(() => {
    proxyRequest.mockReset()
    proxyRequest.mockResolvedValue('proxied')
    requireUserSession.mockReset()
    requireUserSession.mockResolvedValue({ user: { name: 'operator' } })
    useRuntimeConfig.mockReset()
    useRuntimeConfig.mockReturnValue({ opsApiBaseUrl: BASE, opsKey: OPS_KEY })
  })

  it('rejects a request with no session before any network call', async () => {
    const handler = await loadHandler()
    requireUserSession.mockRejectedValue(
      Object.assign(new Error('Unauthorized'), { statusCode: 401 }),
    )

    await expect(handler(eventAt('/api/ops/pipeline-health'))).rejects.toThrow('Unauthorized')

    // The order matters more than the status: a proxy that authenticated *after*
    // forwarding would already have spent the ops key on an anonymous caller's request.
    expect(proxyRequest).not.toHaveBeenCalled()
  })

  it('checks the session before reading the ops key at all', async () => {
    const handler = await loadHandler()
    requireUserSession.mockRejectedValue(new Error('Unauthorized'))

    await expect(handler(eventAt('/api/ops/crashes'))).rejects.toThrow()

    expect(useRuntimeConfig).not.toHaveBeenCalled()
  })

  it('forwards a nominal path to ${base}/ops<path> with the ops key header', async () => {
    const handler = await loadHandler()

    await handler(eventAt('/api/ops/data-quality/detectors'))

    expect(proxyRequest).toHaveBeenCalledTimes(1)
    const [, target, options] = proxyRequest.mock.calls[0]!
    expect(target).toBe(`${BASE}/ops/data-quality/detectors`)
    expect((options as { headers: Record<string, string> }).headers['X-Ops-Key']).toBe(OPS_KEY)
  })

  it('preserves the query string the panel appended to the path', async () => {
    const handler = await loadHandler()

    await handler(eventAt('/api/ops/logs?level=Error&pageSize=50'))

    expect(proxyRequest.mock.calls[0]![1]).toBe(`${BASE}/ops/logs?level=Error&pageSize=50`)
  })

  it('proxies the collection root when the path is empty', async () => {
    const handler = await loadHandler()

    await handler(eventAt('/api/ops'))

    expect(proxyRequest.mock.calls[0]![1]).toBe(`${BASE}/ops`)
  })

  it('never puts the ops key in the URL, only in the header', async () => {
    const handler = await loadHandler()

    await handler(eventAt('/api/ops/configuration'))

    const [, target, options] = proxyRequest.mock.calls[0]!
    // A key in the target would end up in the backend's access log, and in any redirect
    // the proxy followed.
    expect(String(target)).not.toContain(OPS_KEY)
    expect(JSON.stringify(options)).toContain(OPS_KEY)
  })

  it.each([
    '/api/ops/../../etc/passwd',
    '/api/ops//evil.example.com/ops/crashes',
    '/api/ops/https://evil.example.com/ops/crashes',
  ])('refuses to forward %s anywhere', async (path) => {
    const handler = await loadHandler()

    await expect(handler(eventAt(path))).rejects.toMatchObject({ statusCode: 400 })

    // The point of this suite's version of the check: whatever the validation decides, an
    // escaping path must not reach the network carrying the key. (The path predicate
    // itself is pinned separately.)
    expect(proxyRequest).not.toHaveBeenCalled()
  })

  it.each([
    ['not a url at all', 'misconfigured'],
    ['file:///etc/passwd', 'http(s)'],
  ])('fails closed on a %s base URL', async (opsApiBaseUrl, expected) => {
    const handler = await loadHandler()
    useRuntimeConfig.mockReturnValue({ opsApiBaseUrl, opsKey: OPS_KEY })

    await expect(handler(eventAt('/api/ops/crashes'))).rejects.toMatchObject({
      statusCode: 500,
      statusMessage: expect.stringContaining(expected),
    })

    expect(proxyRequest).not.toHaveBeenCalled()
  })
})
