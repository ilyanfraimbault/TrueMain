import { afterEach, describe, expect, it, vi } from 'vitest'

/**
 * `/api/static/{championId}` caches `loadStaticData` for an hour, on the server
 * and in the visitor's browser. An unknown champion is an answer; a failing Data
 * Dragon must throw, or a blip is kept as a nameless champion for that hour.
 */

const VERSIONS = ['16.5.1']
const CHAMPIONS = { data: { Ahri: { id: 'Ahri', key: '103', name: 'Ahri', image: { full: 'Ahri.png' } } } }

async function loadWith(route: (url: string) => Promise<unknown>) {
  vi.resetModules()
  vi.stubGlobal('$fetch', vi.fn(route))
  vi.stubGlobal('createError', (opts: { statusCode: number, statusMessage?: string }) =>
    Object.assign(new Error(opts.statusMessage ?? 'error'), opts))
  vi.stubGlobal('defineCachedFunction', (fn: unknown) => fn)
  const { loadStaticData } = await import('~~/server/utils/ddragon-loader')
  return loadStaticData
}

const ddragon = (overrides: { champions?: () => Promise<unknown>, detail?: () => Promise<unknown> } = {}) =>
  (url: string): Promise<unknown> => {
    if (url.endsWith('/api/versions.json')) return Promise.resolve(VERSIONS)
    if (url.endsWith('/champion.json')) return overrides.champions?.() ?? Promise.resolve(CHAMPIONS)
    if (url.endsWith('/champion/Ahri.json')) return overrides.detail?.() ?? Promise.resolve({ data: { Ahri: { partype: 'Mana', spells: [] } } })
    return Promise.reject(new Error(`unexpected ${url}`))
  }

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('loadStaticData', () => {
  it('names a champion Data Dragon lists', async () => {
    const load = await loadWith(ddragon())
    await expect(load(103, null)).resolves.toMatchObject({ championName: 'Ahri', partype: 'Mana' })
  })

  it('answers an unknown champion with empty data', async () => {
    const load = await loadWith(ddragon())
    await expect(load(999, null)).resolves.toMatchObject({ championName: null })
  })

  it.each([
    ['the champion list', { champions: () => Promise.reject(new Error('503')) }],
    ['the champion detail', { detail: () => Promise.reject(new Error('503')) }],
  ])('throws when %s fails, so nothing is cached', async (_label, overrides) => {
    const load = await loadWith(ddragon(overrides))
    await expect(load(103, null)).rejects.toThrow()
  })
})
