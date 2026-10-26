import { beforeEach, describe, expect, it } from 'vitest'

/**
 * `/api/truemain-identity/{nameTag}` (#2335, #2336): the status a profile page
 * forwards to crawlers and the slug its canonical is built from. An unknown
 * account must be a 404, an API outage must never be one, and only the account's
 * own state may be cached.
 *
 * Same harness as the champion summary handler's: Nitro's auto-imported globals
 * are seeded before the module is evaluated, and the cache stub stores a resolved
 * value and nothing that throws.
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

const profileOf = (gameName: string, tagLine: string | null) => () =>
  Promise.resolve({ identity: { gameName, tagLine, platformId: 'EUW1' } })

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
  getRouterParam: (event: { params: Record<string, string> }, name: string, options?: { decode?: boolean }) =>
    options?.decode ? decodeURIComponent(event.params[name]!) : event.params[name],
  getRequestHeader: (_event: unknown, name: string) => visitorHeaders[name],
  createError: (input: { statusCode: number, statusMessage: string }) => Object.assign(new Error(input.statusMessage), input),
})

const { default: handler } = await import('~~/server/api/truemain-identity/[nameTag].get') as {
  default: (event: unknown) => Promise<{ nameTag: string }>
}

function lookup(nameTag: string) {
  return handler({ params: { nameTag } })
}

async function statusOf(promise: Promise<unknown>) {
  return promise.then(() => 200, (error: { statusCode?: number }) => error.statusCode)
}

describe('truemain identity handler', () => {
  beforeEach(() => {
    calls.length = 0
    cache.clear()
    visitorHeaders = {}
    upstream = () => Promise.reject(httpError(404))
  })

  it('answers the slug of the Riot ID as stored, whatever casing the URL used', async () => {
    upstream = profileOf('Known Ghoster', 'False')

    await expect(lookup('known%20ghoster-false')).resolves.toEqual({ nameTag: 'Known Ghoster-False' })
    // Decoded once, encoded once: the backend sees the slug, not a double-encoded one.
    expect(calls[0]!.path).toBe('/api/truemains/known%20ghoster-false/profile')
  })

  it('keeps the bare game name of an account stored without a tag line', async () => {
    upstream = profileOf('Faker', null)

    await expect(lookup('Faker')).resolves.toEqual({ nameTag: 'Faker' })
  })

  it('answers 404 for an unknown account, and caches it', async () => {
    expect(await statusOf(lookup('nobody-0000'))).toBe(404)
    expect(await statusOf(lookup('nobody-0000'))).toBe(404)

    expect(calls).toHaveLength(1)
  })

  it('treats a profile without an identity as unknown', async () => {
    upstream = () => Promise.resolve({})

    expect(await statusOf(lookup('nobody-0000'))).toBe(404)
  })

  it.each([
    ['a 429', () => Promise.reject(httpError(429))],
    ['a 503', () => Promise.reject(httpError(503))],
    ['an unreachable API', () => Promise.reject(new TypeError('fetch failed'))],
  ])('answers 502, never 404, on %s, and caches nothing', async (_label, failure) => {
    upstream = failure

    expect(await statusOf(lookup('Known Ghoster-False'))).toBe(502)

    upstream = profileOf('Known Ghoster', 'False')
    await expect(lookup('Known Ghoster-False')).resolves.toEqual({ nameTag: 'Known Ghoster-False' })
    expect(calls).toHaveLength(2)
  })

  it('forwards the visitor\'s X-Forwarded-For', async () => {
    visitorHeaders = { 'x-forwarded-for': '203.0.113.7' }
    upstream = profileOf('Known Ghoster', 'False')

    await lookup('Known Ghoster-False')

    expect(calls[0]!.headers).toEqual({ 'x-forwarded-for': '203.0.113.7' })
  })

  it.each(['', '   ', 'x'.repeat(81)])('rejects a malformed slug (%j) without calling the API', async (nameTag) => {
    expect(await statusOf(lookup(nameTag))).toBe(400)
    expect(calls).toHaveLength(0)
  })
})
