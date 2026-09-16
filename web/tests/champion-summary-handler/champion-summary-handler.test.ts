import { beforeEach, describe, expect, it } from 'vitest'

/**
 * The champion page's SSR build summary (#1557): its fan-out must be attributed to
 * the visitor who caused it, and a throttled or failing upstream must never be
 * cached as the slice's answer for everyone.
 *
 * The handler is written against Nitro's auto-imported globals. They are seeded
 * before the module is evaluated; the cache stub behaves like Nitro's on the one
 * property under test — a resolved value is stored, a thrown one is not.
 */

interface FetchCall { path: string, headers?: Record<string, string> }
type Upstream = (path: string) => Promise<unknown>

const calls: FetchCall[] = []
const cache = new Map<string, unknown>()
let upstream: Upstream
let visitorHeaders: Record<string, string> = {}

function httpError(statusCode: number) {
  return Object.assign(new Error(`HTTP ${statusCode}`), { statusCode })
}

const notFoundEverywhere: Upstream = () => Promise.reject(httpError(404))

Object.assign(globalThis, {
  $fetch: (path: string, options?: { headers?: Record<string, string> }) => {
    calls.push({ path, headers: options?.headers })
    return upstream(path)
  },
  defineCachedFunction: <A extends unknown[], R>(
    fn: (...args: A) => Promise<R>,
    options: { getKey: (...args: A) => string },
  ) => async (...args: A) => {
    const key = options.getKey(...args)
    if (cache.has(key)) return cache.get(key) as R
    const value = await fn(...args)
    cache.set(key, value)
    return value
  },
  defineEventHandler: <T>(handler: T) => handler,
  getRouterParam: (event: { params: Record<string, string> }, name: string) => event.params[name],
  getQuery: (event: { query: Record<string, string> }) => event.query,
  getRequestHeader: (_event: unknown, name: string) => visitorHeaders[name],
  createError: (input: { statusCode: number, statusMessage: string }) => Object.assign(new Error(input.statusMessage), input),
})

const { default: handler } = await import('~~/server/api/champion-summary/[championId].get') as {
  default: (event: unknown) => Promise<unknown>
}

function viewChampion(championId = '103') {
  return handler({ params: { championId }, query: { position: 'MIDDLE' } })
}

function championCalls() {
  return calls.filter(call => call.path === '/api/champions/103')
}

describe('champion summary handler', () => {
  beforeEach(() => {
    calls.length = 0
    cache.clear()
    visitorHeaders = {}
    upstream = notFoundEverywhere
  })

  it('forwards the visitor\'s X-Forwarded-For to every upstream call', async () => {
    visitorHeaders = { 'x-forwarded-for': '203.0.113.7, 172.18.0.5' }

    await viewChampion()

    expect(calls.length).toBeGreaterThan(0)
    expect(calls.every(call => call.headers?.['x-forwarded-for'] === '203.0.113.7, 172.18.0.5')).toBe(true)
  })

  it('sends no forwarded header when the visitor request carried none', async () => {
    await viewChampion()

    expect(calls.every(call => call.headers === undefined)).toBe(true)
  })

  it('caches a 404, which is the slice\'s answer', async () => {
    await viewChampion()
    await viewChampion()

    expect(championCalls()).toHaveLength(1)
  })

  it.each([429, 503])('answers a %i with an empty summary and caches nothing', async (status) => {
    upstream = path => path === '/api/champions/103'
      ? Promise.reject(httpError(status))
      : notFoundEverywhere(path)

    await expect(viewChampion()).resolves.toBeTruthy()

    upstream = notFoundEverywhere
    await viewChampion()

    expect(championCalls()).toHaveLength(2)
  })

  it('does not cache an unreachable upstream either', async () => {
    upstream = path => path === '/api/champions/103'
      ? Promise.reject(new TypeError('fetch failed'))
      : notFoundEverywhere(path)

    await viewChampion()
    upstream = notFoundEverywhere
    await viewChampion()

    expect(championCalls()).toHaveLength(2)
  })
})
