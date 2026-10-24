import { connect, createServer, type AddressInfo } from 'node:net'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createLineServer, edgeLogPort, toEdgeProxyError } from '~~/server/utils/edge-error-log'

// The plugin is a Nitro plugin: its helpers are auto-imports, so they are stubbed as globals
// here — with the real line server and parser, and a recording forwarder in place of the one
// that would post to the API.
const report = vi.fn()
const stop = vi.fn(async () => {})
const startIngestForwarder = vi.fn(() => ({ forwarder: { report }, stop }))
const useRuntimeConfig = vi.fn()

vi.stubGlobal('defineNitroPlugin', (plugin: unknown) => plugin)
vi.stubGlobal('useRuntimeConfig', () => useRuntimeConfig())
vi.stubGlobal('startIngestForwarder', startIngestForwarder)
vi.stubGlobal('createLineServer', createLineServer)
vi.stubGlobal('edgeLogPort', edgeLogPort)
vi.stubGlobal('toEdgeProxyError', toEdgeProxyError)

type CloseHook = () => Promise<void>

async function runPlugin(config: Record<string, unknown>): Promise<CloseHook | null> {
  useRuntimeConfig.mockReturnValue({ opsApiBaseUrl: 'http://api:8080', ...config })
  const plugin = (await import('~~/server/plugins/edge-error-relay')).default as unknown as (app: unknown) => void
  let close: CloseHook | null = null
  plugin({ hooks: { hook: (_: string, handler: CloseHook) => { close = handler } } })
  return close
}

async function freePort(): Promise<number> {
  const probe = createServer()
  await new Promise<void>(resolve => probe.listen(0, '127.0.0.1', resolve))
  const { port } = probe.address() as AddressInfo
  await new Promise(resolve => probe.close(resolve))
  return port
}

describe('edge error relay plugin', () => {
  beforeEach(() => {
    report.mockClear()
    stop.mockClear()
    startIngestForwarder.mockClear()
  })

  it('stays off without a port', async () => {
    expect(await runPlugin({ logIngestKey: 'k'.repeat(32), edgeLogPort: '' })).toBeNull()
    expect(startIngestForwarder).not.toHaveBeenCalled()
  })

  it.each([
    [{ logIngestKey: 'k'.repeat(32), edgeLogPort: 'nope' }],
    [{ logIngestKey: '', edgeLogPort: '5140' }],
  ])('stays off, and says why, with %j', async (config) => {
    const error = vi.spyOn(console, 'error').mockImplementation(() => {})
    expect(await runPlugin(config)).toBeNull()
    expect(startIngestForwarder).not.toHaveBeenCalled()
    expect(error).toHaveBeenCalledOnce()
    error.mockRestore()
  })

  it('relays the edge failures it receives under process Edge, and stops on close', async () => {
    const port = await freePort()
    const close = await runPlugin({ logIngestKey: 'k'.repeat(32), edgeLogPort: String(port) })

    expect(startIngestForwarder).toHaveBeenCalledWith({ process: 'Edge', apiBaseUrl: 'http://api:8080', logIngestKey: 'k'.repeat(32) })
    const socket = connect(port, '127.0.0.1')
    await new Promise<void>(resolve => socket.once('connect', resolve))
    socket.write('{"logger":"http.log.error.web","msg":"dial tcp 172.18.0.5:3000: i/o timeout","status":502}\n')
    socket.write('{"logger":"http.log.access.web","status":503}\n')
    await new Promise<void>(resolve => socket.end(resolve))
    await vi.waitFor(() => expect(report).toHaveBeenCalledOnce())
    expect(report).toHaveBeenCalledWith(expect.objectContaining({ eventType: 'EdgeProxyError', statusCode: 502 }))

    await close!()
    expect(stop).toHaveBeenCalledOnce()
  })
})
