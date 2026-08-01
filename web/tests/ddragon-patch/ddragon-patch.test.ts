import { afterEach, describe, expect, it, vi } from 'vitest'

// `resolveLatestDDragonPatch` is built at import time by the auto-imported
// `defineCachedFunction`, so the globals have to be stubbed before the module
// is loaded — hence the dynamic import. Nitro owns the memoization itself;
// what these tests pin is the resolver's own behaviour and the cache options
// we register it with, which are the point of #947.
interface CacheRegistration {
  options: { maxAge?: number, name?: string, getKey?: (...args: unknown[]) => string }
}

const registrations: CacheRegistration[] = []

async function loadResolver(fetchImpl: () => Promise<string[]>) {
  vi.resetModules()
  registrations.length = 0
  vi.stubGlobal('$fetch', vi.fn(fetchImpl))
  vi.stubGlobal('createError', (opts: { statusCode: number, statusMessage?: string }) =>
    Object.assign(new Error(opts.statusMessage ?? 'error'), opts))
  vi.stubGlobal('defineCachedFunction', (fn: unknown, options: CacheRegistration['options']) => {
    registrations.push({ options })
    return fn
  })
  const mod = await import('~~/server/utils/ddragon-patch')
  return { resolve: mod.resolveLatestDDragonPatch, registration: registrations[0] }
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('resolveLatestDDragonPatch', () => {
  it('returns the newest version DDragon lists', async () => {
    const { resolve } = await loadResolver(async () => ['16.5.1', '16.4.1'])
    await expect(resolve()).resolves.toBe('16.5.1')
  })

  it('throws a 502 when DDragon returns no versions', async () => {
    const { resolve } = await loadResolver(async () => [])
    await expect(resolve()).rejects.toMatchObject({ statusCode: 502 })
  })

  it('is cached under a constant key, well beyond the 1 h payload TTL', async () => {
    const { registration } = await loadResolver(async () => ['16.5.1'])
    // A per-call key would defeat the cache: the resolver takes no arguments,
    // so every static endpoint must land on the same entry.
    expect(registration?.options.getKey?.()).toBe('latest')
    // Patches ship every ~2 weeks. Anything at or under the payload TTL would
    // leave the lookup on the critical path roughly as often as before.
    expect(registration?.options.maxAge).toBeGreaterThan(60 * 60)
  })
})
