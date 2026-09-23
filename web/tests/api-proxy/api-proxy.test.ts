import type { EventHandler, H3Event } from 'h3'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { EventEmitter } from 'node:events'
import { abortOnAbandonment } from '~~/server/utils/abandoned-request'
import { isInternalApiPath } from '~~/server/utils/internal-api-path'
import { toRouteTemplate } from '~~/server/utils/log-forwarder'
import { isUnsafeProxyPath } from '~~/server/utils/proxy-path'

// The public `/api` proxy (#1556): it must never forward a visitor to the API's
// `/internal` surface, and every 429 or 5xx the API answers through it is reported to
// the ops logs. Nitro auto-imports are stubbed as globals, the guards with their real
// implementations; `proxyRequest` is mocked so nothing opens a socket.
const proxyRequest = vi.fn()

vi.mock('h3', async (importOriginal) => {
  const actual = await importOriginal<typeof import('h3')>()
  return { ...actual, proxyRequest: (...args: unknown[]) => proxyRequest(...args) }
})

const reportToOpsLogs = vi.fn()

vi.stubGlobal('useRuntimeConfig', () => ({ apiBaseUrl: 'http://api:8080' }))
vi.stubGlobal('isUnsafeProxyPath', isUnsafeProxyPath)
vi.stubGlobal('isInternalApiPath', isInternalApiPath)
vi.stubGlobal('toRouteTemplate', toRouteTemplate)
vi.stubGlobal('reportToOpsLogs', reportToOpsLogs)
vi.stubGlobal('abortOnAbandonment', abortOnAbandonment)

async function loadHandler(): Promise<EventHandler> {
  const module = await import('~~/server/api/[...path]')
  return module.default as EventHandler
}

function eventAt(path: string): H3Event {
  return { path, method: 'GET' } as H3Event
}

function onResponseOfFirstCall(): (event: H3Event, response: Response) => void {
  const [, , options] = proxyRequest.mock.calls[0]!
  return (options as { onResponse: (event: H3Event, response: Response) => void }).onResponse
}

function eventWithResponseAt(path: string) {
  const res = Object.assign(new EventEmitter(), { writableFinished: false })
  return { event: { path, method: 'GET', node: { res } } as unknown as H3Event, res }
}

describe('api proxy handler', () => {
  beforeEach(() => {
    proxyRequest.mockReset()
    proxyRequest.mockResolvedValue('proxied')
    reportToOpsLogs.mockReset()
  })

  it.each([
    '/api/internal/logs',
    '/api/INTERNAL/logs',
    '/api/%69nternal/logs',
    '/api/./internal/logs',
    '/api/internal',
  ])('refuses %s before any network call', async (path) => {
    const handler = await loadHandler()

    await expect(handler(eventAt(path))).rejects.toMatchObject({ statusCode: 404 })

    expect(proxyRequest).not.toHaveBeenCalled()
  })

  it('still forwards a path that merely contains the word', async () => {
    const handler = await loadHandler()

    await handler(eventAt('/api/truemains/internal-EUW'))

    expect(proxyRequest.mock.calls[0]![1]).toBe('http://api:8080/truemains/internal-EUW')
  })

  it.each([
    [503, 'Error'],
    [429, 'Warning'],
  ])('reports a %i from the API per route template', async (status, level) => {
    const handler = await loadHandler()
    await handler(eventAt('/api/champions/103/scaling?position=MIDDLE'))

    onResponseOfFirstCall()(eventAt('/api/champions/103/scaling?position=MIDDLE'), new Response(null, { status }))

    expect(reportToOpsLogs).toHaveBeenCalledWith(expect.objectContaining({
      level,
      eventType: 'FrontendUpstreamErrors',
      requestPath: '/api/champions/{n}/scaling',
      statusCode: status,
    }))
  })

  it('reports nothing for an answer the visitor was meant to get', async () => {
    const handler = await loadHandler()
    await handler(eventAt('/api/champions/103'))

    onResponseOfFirstCall()(eventAt('/api/champions/103'), new Response(null, { status: 404 }))

    expect(reportToOpsLogs).not.toHaveBeenCalled()
  })

  // #1569: the upstream call is cancelled with the visitor, and that cancellation is
  // not a proxy failure.
  it('cancels the API call when the visitor leaves, and returns quietly', async () => {
    const handler = await loadHandler()
    const { event, res } = eventWithResponseAt('/api/champions/103/scaling')
    proxyRequest.mockImplementation(async (_event, _target, options: { fetchOptions: { signal: AbortSignal } }) => {
      res.emit('close')
      expect(options.fetchOptions.signal.aborted).toBe(true)
      throw new Error('This operation was aborted')
    })

    await expect(handler(event)).resolves.toBeNull()
  })

  it('still fails when the API call fails with the visitor waiting', async () => {
    const handler = await loadHandler()
    const { event } = eventWithResponseAt('/api/champions/103/scaling')
    proxyRequest.mockRejectedValue(Object.assign(new Error('Bad Gateway'), { statusCode: 502 }))

    await expect(handler(event)).rejects.toMatchObject({ statusCode: 502 })
  })
})
