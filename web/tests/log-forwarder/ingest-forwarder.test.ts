import { afterEach, describe, expect, it, vi } from 'vitest'
import { startIngestForwarder } from '~~/layers/nitro/server/utils/ingest-forwarder'

const fetchMock = vi.fn()
vi.stubGlobal('$fetch', fetchMock)

const report = {
  level: 'Error',
  category: 'edge',
  eventType: 'EdgeProxyError',
  message: 'The edge proxy could not serve a request to web (502): dial tcp 172.18.0.5:3000: i/o timeout',
  statusCode: 502,
} as const

describe('startIngestForwarder', () => {
  afterEach(() => {
    fetchMock.mockReset()
    vi.useRealTimers()
  })

  it('posts what was reported to the ingest endpoint with its key, under its process, on every tick', async () => {
    vi.useFakeTimers()
    fetchMock.mockResolvedValue({ accepted: 1 })
    const { forwarder, stop } = startIngestForwarder({ process: 'Edge', apiBaseUrl: 'http://api:8080', logIngestKey: 'secret' })

    forwarder.report(report)
    forwarder.report(report)
    await vi.advanceTimersByTimeAsync(5_000)

    expect(fetchMock).toHaveBeenCalledOnce()
    const [url, options] = fetchMock.mock.calls[0]!
    expect(url).toBe('http://api:8080/internal/logs')
    expect(options).toMatchObject({ method: 'POST', headers: { 'X-Log-Ingest-Key': 'secret' }, retry: 0 })
    expect(options.body.process).toBe('Edge')
    expect(options.body.entries).toEqual([expect.objectContaining({ eventType: 'EdgeProxyError', count: 2 })])

    await stop()
    await vi.advanceTimersByTimeAsync(10_000)
    expect(fetchMock).toHaveBeenCalledOnce()
  })

  it('sends what is pending when stopped, and logs a failed send instead of throwing', async () => {
    const error = vi.spyOn(console, 'error').mockImplementation(() => {})
    fetchMock.mockRejectedValue(new Error('connect ECONNREFUSED'))
    const { forwarder, stop } = startIngestForwarder({ process: 'Admin', apiBaseUrl: 'http://api:8080', logIngestKey: 'secret' })

    forwarder.report(report)
    await stop()

    expect(fetchMock).toHaveBeenCalledOnce()
    expect(error).toHaveBeenCalledWith('[log-forwarder] could not forward Admin errors to the API:', 'connect ECONNREFUSED')
    error.mockRestore()
  })
})
