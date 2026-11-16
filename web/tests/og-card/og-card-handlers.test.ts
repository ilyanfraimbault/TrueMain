import { beforeEach, describe, expect, it } from 'vitest'

/**
 * The share-card routes (#926) cache for an hour, so they follow the #1557 rule:
 * an absence is cached, a throttled or failing upstream is not, and the fan-out
 * carries the visitor's address. The cache stub behaves like Nitro's on the one
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
    options: { name: string, getKey: (...args: A) => string },
  ) => async (...args: A) => {
    const key = `${options.name}:${options.getKey(...args)}`
    if (cache.has(key)) return cache.get(key) as R
    const value = await fn(...args)
    cache.set(key, value)
    return value
  },
  defineEventHandler: <T>(handler: T) => handler,
  getRouterParam: (event: { params: Record<string, string> }, name: string, options?: { decode?: boolean }) =>
    options?.decode ? decodeURIComponent(event.params[name]!) : event.params[name],
  getQuery: (event: { query: Record<string, string> }) => event.query,
  getRequestHeader: (_event: unknown, name: string) => visitorHeaders[name],
  createError: (input: { statusCode: number, statusMessage: string }) => Object.assign(new Error(input.statusMessage), input),
})

type Handler = (event: unknown) => Promise<unknown>
const { default: championCard } = await import('~~/server/api/og/champion/[championId].get') as { default: Handler }
const { default: truemainCard } = await import('~~/server/api/og/truemain/[nameTag].get') as { default: Handler }

const viewChampionCard = () => championCard({ params: { championId: '103' }, query: {} })
const viewTruemainCard = () => truemainCard({ params: { nameTag: 'Faker-KR1' }, query: {} })
const callsTo = (path: string) => calls.filter(call => call.path === path)

const profile = {
  identity: { gameName: 'Faker', tagLine: 'KR1', platformId: 'KR' },
  ranked: null,
  mains: [],
  dedication: null,
}

describe('share-card routes', () => {
  beforeEach(() => {
    calls.length = 0
    cache.clear()
    visitorHeaders = {}
    upstream = notFoundEverywhere
  })

  it('forward the visitor\'s address to every upstream call', async () => {
    visitorHeaders = { 'x-forwarded-for': '203.0.113.7' }

    await viewChampionCard()
    await viewTruemainCard()

    expect(calls.length).toBeGreaterThan(0)
    expect(calls.every(call => call.headers?.['x-forwarded-for'] === '203.0.113.7')).toBe(true)
  })

  it('encode the Riot ID once, as the route received it', async () => {
    await truemainCard({ params: { nameTag: 'Hide%20on%20bush-KR1' }, query: {} })

    expect(callsTo('/api/truemains/Hide%20on%20bush-KR1/profile')).toHaveLength(1)
  })

  it('cache an absence', async () => {
    await viewTruemainCard()
    await viewTruemainCard()

    expect(callsTo('/api/truemains/Faker-KR1/profile')).toHaveLength(1)
  })

  it.each([429, 503])('answer a %i with the branded card and cache nothing', async (status) => {
    upstream = () => Promise.reject(httpError(status))

    await expect(viewTruemainCard()).resolves.toMatchObject({ riotId: null })
    await expect(viewChampionCard()).resolves.toMatchObject({ championName: null, stats: null })

    upstream = path => path === '/api/truemains/Faker-KR1/profile' ? Promise.resolve(profile) : notFoundEverywhere(path)
    await expect(viewTruemainCard()).resolves.toMatchObject({ riotId: 'Faker#KR1' })
    await viewChampionCard()

    expect(callsTo('/api/truemains/Faker-KR1/profile')).toHaveLength(2)
    expect(callsTo('/api/champions')).toHaveLength(2)
  })
})
