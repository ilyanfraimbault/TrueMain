import { afterEach, describe, expect, it, vi } from 'vitest'

// `loadDDragonVersions` is built at import time by the auto-imported
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
  return {
    loadVersions: mod.loadDDragonVersions,
    resolveVersion: mod.resolveDDragonVersion,
    normalizeRequested: mod.normalizeRequestedPatch,
    registration: registrations[0],
  }
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('loadDDragonVersions', () => {
  it('returns the versions DDragon lists, newest first', async () => {
    const { loadVersions } = await loadResolver(async () => ['16.5.1', '16.4.1'])
    await expect(loadVersions()).resolves.toEqual(['16.5.1', '16.4.1'])
  })

  it('throws a 502 when DDragon returns no versions', async () => {
    const { loadVersions } = await loadResolver(async () => [])
    await expect(loadVersions()).rejects.toMatchObject({ statusCode: 502 })
  })

  it('is cached under a constant key, well beyond the 1 h payload TTL', async () => {
    const { registration } = await loadResolver(async () => ['16.5.1'])
    // A per-call key would defeat the cache: the loader takes no arguments,
    // so every static endpoint must land on the same entry.
    expect(registration?.options.getKey?.()).toBe('versions')
    // Patches ship every ~2 weeks. Anything at or under the payload TTL would
    // leave the lookup on the critical path roughly as often as before.
    expect(registration?.options.maxAge).toBeGreaterThan(60 * 60)
  })
})

describe('resolveDDragonVersion', () => {
  it('resolves the newest version when no patch was requested', async () => {
    const { resolveVersion } = await loadResolver(async () => ['16.5.1', '16.4.1'])
    await expect(resolveVersion(null)).resolves.toBe('16.5.1')
  })

  it('pins a patch DDragon publishes to that exact version', async () => {
    const { resolveVersion } = await loadResolver(async () => ['16.5.1', '16.4.1', '16.3.1'])
    // A patch the caller asked for and DDragon has must never be silently
    // upgraded to the latest — the page would then show current assets under an
    // older patch's numbers.
    await expect(resolveVersion('16.4.1')).resolves.toBe('16.4.1')
  })

  it('falls back to the newest version for a patch DDragon has not published yet', async () => {
    const { resolveVersion } = await loadResolver(async () => ['16.18.1', '16.17.1'])
    // The #1693 case: Riot ships 16.19 and match data reports it days before
    // DDragon publishes the folder, whose pinned URL answers 403, not 404.
    await expect(resolveVersion('16.19.1')).resolves.toBe('16.18.1')
  })

  it('matches on major.minor, whatever build number DDragon gave the patch', async () => {
    const { resolveVersion } = await loadResolver(async () => ['16.5.2', '16.4.1'])
    // `normalizeDataDragonPatch` expands "16.5" to "16.5.1", but DDragon
    // occasionally ships a second build — the `.1` guess must not miss it.
    await expect(resolveVersion('16.5.1')).resolves.toBe('16.5.2')
  })

  it('throws a 502 when DDragon returns no versions', async () => {
    const { resolveVersion } = await loadResolver(async () => [])
    await expect(resolveVersion('16.5.1')).rejects.toMatchObject({ statusCode: 502 })
  })
})

describe('normalizeRequestedPatch', () => {
  it('returns null when the caller supplies no patch', async () => {
    const { normalizeRequested } = await loadResolver(async () => ['16.5.1'])
    // The static endpoints fall back to the latest patch on null; an empty
    // `?patch=` must land there too rather than being rejected.
    expect(normalizeRequested(undefined)).toBeNull()
    expect(normalizeRequested('')).toBeNull()
  })

  it('expands the short patch form the backend scopes expose', async () => {
    const { normalizeRequested } = await loadResolver(async () => ['16.5.1'])
    expect(normalizeRequested('16.5')).toBe('16.5.1')
    expect(normalizeRequested('16.5.1')).toBe('16.5.1')
  })

  it('rejects anything that is not major.minor.patch with a 400', async () => {
    const { normalizeRequested } = await loadResolver(async () => ['16.5.1'])
    // The value is interpolated into a CDN URL and used as a cache key, so a
    // traversal payload must never get through, and arbitrary strings must not
    // be allowed to mint an unbounded number of cache entries.
    for (const payload of ['../../etc/passwd', '16.5.1/../../x', 'latest', '16', '16.5.1.2', '16.a.1']) {
      expect(() => normalizeRequested(payload)).toThrowError(expect.objectContaining({ statusCode: 400 }))
    }
  })
})
