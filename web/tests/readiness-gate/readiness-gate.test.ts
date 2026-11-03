import type { EventHandler, H3Event } from 'h3'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createReadinessGate } from '~~/server/utils/readiness-gate'

// The edge's maintenance trigger (#2416): Caddy adds failed probes up over the life of
// the process, so the route it probes must only answer 503 after failures in a row.
describe('readiness gate', () => {
  it('counts as down until the first success', () => {
    const gate = createReadinessGate()

    expect(gate.record(false)).toBe(false)
    expect(gate.record(true)).toBe(true)
  })

  it('stays up through isolated failures, however many', () => {
    const gate = createReadinessGate()
    gate.record(true)

    for (let round = 0; round < 10; round++) {
      expect(gate.record(false)).toBe(true)
      expect(gate.record(true)).toBe(true)
    }
  })

  it('goes down on the third failure in a row, and back up on one success', () => {
    const gate = createReadinessGate()
    gate.record(true)

    expect(gate.record(false)).toBe(true)
    expect(gate.record(false)).toBe(true)
    expect(gate.record(false)).toBe(false)
    expect(gate.record(false)).toBe(false)
    expect(gate.record(true)).toBe(true)
  })
})

describe('readiness route', () => {
  const raw = vi.fn()
  const reportToOpsLogs = vi.fn()

  vi.stubGlobal('useRuntimeConfig', () => ({ apiBaseUrl: 'http://api:8080' }))
  vi.stubGlobal('$fetch', { raw })
  vi.stubGlobal('reportToOpsLogs', reportToOpsLogs)
  vi.stubGlobal('createReadinessGate', createReadinessGate)

  async function freshRoute() {
    vi.resetModules()
    const handler = (await import('~~/server/api/readyz.get')).default as EventHandler
    return async () => {
      const res = { statusCode: 200, setHeader: vi.fn() }
      const body = await handler({ node: { res } } as unknown as H3Event)
      return { status: res.statusCode, body, setHeader: res.setHeader }
    }
  }

  beforeEach(() => {
    raw.mockReset()
    reportToOpsLogs.mockReset()
  })

  it('asks the API\'s readiness, bounded, and is never cached', async () => {
    raw.mockResolvedValue({ status: 200 })
    const probe = await freshRoute()

    const answer = await probe()

    expect(answer).toMatchObject({ status: 200, body: 'ok' })
    expect(answer.setHeader).toHaveBeenCalledWith('Cache-Control', 'no-store')
    expect(raw).toHaveBeenCalledWith('http://api:8080/readyz', expect.objectContaining({ timeout: 8000, retry: false }))
    expect(reportToOpsLogs).not.toHaveBeenCalled()
  })

  it('answers 200 through two failures in a row, 503 on the third, and logs each', async () => {
    const probe = await freshRoute()
    raw.mockResolvedValueOnce({ status: 200 })
    await probe()

    raw.mockResolvedValueOnce({ status: 503 })
    expect((await probe()).status).toBe(200)
    raw.mockRejectedValueOnce(new Error('timeout'))
    expect((await probe()).status).toBe(200)
    raw.mockResolvedValueOnce({ status: 503 })
    expect(await probe()).toMatchObject({ status: 503, body: 'down' })

    expect(reportToOpsLogs).toHaveBeenCalledTimes(3)
    expect(reportToOpsLogs.mock.calls[1]![0]).toMatchObject({
      message: 'The API\'s readiness check failed (no answer)',
      statusCode: null,
    })
  })

  it('answers 503 from a fresh start until the API has answered', async () => {
    const probe = await freshRoute()

    raw.mockRejectedValueOnce(new Error('refused'))
    expect((await probe()).status).toBe(503)
    raw.mockResolvedValueOnce({ status: 200 })
    expect((await probe()).status).toBe(200)
  })
})
