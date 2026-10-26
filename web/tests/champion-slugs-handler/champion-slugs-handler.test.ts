import { describe, expect, it, vi } from 'vitest'

/**
 * `/api/static/champion-slugs` (#1124), shared by the site and the admin since
 * #2291: the slug is DDragon's key lower-cased, keyed by the numeric id, and
 * alternate-mode entries never become linkable pages. Nitro's globals are seeded
 * before the module is evaluated.
 */

const fetched: string[] = []

vi.mock('~~/layers/nitro/server/utils/ddragon-patch', () => ({
  resolveDDragonVersion: async () => '16.21.1',
}))

Object.assign(globalThis, {
  $fetch: async (url: string) => {
    fetched.push(url)
    return {
      data: {
        Ahri: { id: 'Ahri', key: '103' },
        MasterYi: { id: 'MasterYi', key: '11' },
        Strawberry: { id: 'Strawberry_Ahri', key: '103001' },
      },
    }
  },
  defineCachedFunction: <A extends unknown[], R>(fn: (...args: A) => Promise<R>) => fn,
  defineEventHandler: <T>(handler: T) => handler,
})

const { default: handler } = await import('~~/layers/nitro/server/api/static/champion-slugs.get') as {
  default: () => Promise<Record<string, string>>
}

describe('champion slugs endpoint', () => {
  it('maps every live champion id to its lower-cased DDragon key', async () => {
    expect(await handler()).toEqual({ 103: 'ahri', 11: 'masteryi' })
  })

  it('reads the champion list of the resolved patch', async () => {
    await handler()
    expect(fetched.at(-1)).toBe('https://ddragon.leagueoflegends.com/cdn/16.21.1/data/en_US/champion.json')
  })
})
