import { describe, expect, it, vi } from 'vitest'
import type { ForwardedBatch, ForwardedError } from '~~/server/utils/log-forwarder'
import { createLogForwarder, errorStatus, toRouteTemplate } from '~~/server/utils/log-forwarder'

// Pins server/utils/log-forwarder.ts, which is duplicated file-for-file between web/
// and admin/: both suites carry this test so neither copy can drift (#1556).

const NOW = new Date('2026-09-15T12:00:00.000Z')

function error(overrides: Partial<ForwardedError> = {}): ForwardedError {
  return {
    level: 'Error',
    category: 'nitro',
    eventType: 'FrontendServerError',
    message: 'boom',
    requestMethod: 'GET',
    requestPath: '/champions/{n}',
    statusCode: 500,
    ...overrides,
  }
}

function forwarder(options: { maxPending?: number, send?: (batch: ForwardedBatch) => Promise<unknown> } = {}) {
  const batches: ForwardedBatch[] = []
  const onSendError = vi.fn()
  const instance = createLogForwarder({
    process: 'Web',
    host: 'host-1',
    now: () => NOW,
    maxPending: options.maxPending,
    send: options.send ?? (async (batch) => {
      batches.push(batch)
    }),
    onSendError,
  })
  return { instance, batches, onSendError }
}

describe('log forwarder', () => {
  it('folds identical errors into one entry with a count', async () => {
    const { instance, batches } = forwarder()

    for (let index = 0; index < 5; index++) instance.report(error())
    instance.report(error({ statusCode: 502 }))
    await instance.flush()

    expect(batches).toHaveLength(1)
    expect(batches[0]!.process).toBe('Web')
    expect(batches[0]!.entries.map(entry => [entry.statusCode, entry.count])).toEqual([[500, 5], [502, 1]])
    expect(batches[0]!.entries[0]!.timestampUtc).toBe(NOW.toISOString())
  })

  it('never sends more than 50 entries in one request', async () => {
    const { instance, batches } = forwarder()

    for (let index = 0; index < 120; index++) instance.report(error({ message: `distinct ${index}` }))
    await instance.flush()

    expect(batches.map(batch => batch.entries.length)).toEqual([50, 50, 20])
  })

  it('counts, rather than keeps, distinct errors past the bound', async () => {
    const { instance, batches } = forwarder({ maxPending: 3 })

    for (let index = 0; index < 7; index++) instance.report(error({ message: `distinct ${index}` }))
    await instance.flush()

    const entries = batches.flatMap(batch => batch.entries)
    expect(entries).toHaveLength(4)
    expect(entries[3]!.level).toBe('Warning')
    expect(entries[3]!.message).toContain('4 error report(s) were not forwarded')
  })

  it('drops a batch the API did not take, without throwing or retrying', async () => {
    const send = vi.fn().mockRejectedValue(new Error('connect ECONNREFUSED'))
    const { instance, onSendError } = forwarder({ send })

    instance.report(error())
    await expect(instance.flush()).resolves.toBeUndefined()
    await instance.flush()

    expect(send).toHaveBeenCalledTimes(1)
    expect(onSendError).toHaveBeenCalledTimes(1)
    expect(instance.pendingCount).toBe(0)
  })

  it('sends nothing when nothing was reported', async () => {
    const send = vi.fn()
    const { instance } = forwarder({ send })

    await instance.flush()

    expect(send).not.toHaveBeenCalled()
  })

  it('truncates what the API would refuse', async () => {
    const { instance, batches } = forwarder()

    instance.report(error({ message: 'x'.repeat(10_000), exception: 'y'.repeat(20_000) }))
    await instance.flush()

    expect(batches[0]!.entries[0]!.message).toHaveLength(4_000)
    expect(batches[0]!.entries[0]!.exception).toHaveLength(16_000)
  })
})

describe('toRouteTemplate', () => {
  it.each([
    ['/api/champions/103/scaling?position=MIDDLE', '/api/champions/{n}/scaling'],
    ['/api/truemains/Faker-KR1/matches?page=2', '/api/truemains/{nameTag}/matches'],
    ['/truemains/Faker-KR1', '/truemains/{nameTag}'],
    ['/api/truemains/search?q=fak', '/api/truemains/search'],
    ['/champions/ahri', '/champions/ahri'],
  ])('%s → %s', (path, template) => {
    expect(toRouteTemplate(path)).toBe(template)
  })

  it('is null without a path', () => {
    expect(toRouteTemplate(undefined)).toBeNull()
  })
})

describe('errorStatus', () => {
  it('reads statusCode or status, and treats anything else as a 500', () => {
    expect(errorStatus({ statusCode: 502 })).toBe(502)
    expect(errorStatus({ status: 404 })).toBe(404)
    expect(errorStatus(new Error('boom'))).toBe(500)
  })
})
