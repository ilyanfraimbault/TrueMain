import { beforeAll, describe, expect, it, vi } from 'vitest'
import { pageParams, resolveDevApiMock, tierFor } from '~~/server/utils/dev-api-mock'

// `resolveDevApiMock` reaches two auto-imported server globals: `$fetch`
// (via latestShortPatch, for the DDragon version list) and `createError`
// (thrown as the 404s). Stub both so routing is exercised deterministically
// without the network, and so a thrown 404 carries an assertable statusCode.
beforeAll(() => {
  vi.stubGlobal('$fetch', vi.fn(async () => ['15.13.1', '15.12.1']))
  vi.stubGlobal('createError', (opts: { statusCode: number, statusMessage?: string }) =>
    Object.assign(new Error(opts.statusMessage ?? 'error'), opts))
})

describe('tierFor', () => {
  it('buckets by win-rate percentile, S at the top through D at the bottom', () => {
    expect(tierFor(0, 100)).toBe('S')
    expect(tierFor(20, 100)).toBe('A')
    expect(tierFor(50, 100)).toBe('B')
    expect(tierFor(80, 100)).toBe('C')
    expect(tierFor(99, 100)).toBe('D')
  })
})

describe('pageParams', () => {
  it('defaults to page 1 and the fallback size', () => {
    expect(pageParams({}, 25, 100)).toEqual({ page: 1, pageSize: 25 })
  })

  it('clamps page up to 1 and pageSize down to the max', () => {
    expect(pageParams({ page: '-3', pageSize: '999' }, 25, 100)).toEqual({ page: 1, pageSize: 100 })
  })

  it('clamps a negative pageSize up to 1', () => {
    expect(pageParams({ page: '2', pageSize: '-5' }, 25, 100)).toEqual({ page: 2, pageSize: 1 })
  })

  it('clamps an explicit pageSize=0 up to 1 (not the fallback)', () => {
    expect(pageParams({ pageSize: '0' }, 25, 100)).toEqual({ page: 1, pageSize: 1 })
  })

  it('falls back on non-numeric input', () => {
    expect(pageParams({ page: 'x', pageSize: 'y' }, 25, 100)).toEqual({ page: 1, pageSize: 25 })
  })
})

describe('resolveDevApiMock', () => {
  it('returns undefined for a path the mock does not serve', async () => {
    expect(await resolveDevApiMock('/not/a/route', {})).toBeUndefined()
    expect(await resolveDevApiMock('/champions/64/nonsense', {})).toBeUndefined()
  })

  it('routes /champions to the champion list', async () => {
    const res = await resolveDevApiMock('/champions', {})
    expect(Array.isArray(res)).toBe(true)
    expect((res as unknown[]).length).toBeGreaterThan(0)
  })

  it('resolves a known player route (Sheiden-1234)', async () => {
    const res = await resolveDevApiMock('/truemains/Sheiden-1234/profile', {})
    expect(res).toBeTruthy()
    expect(res).toHaveProperty('identity')
  })

  it('404s on an unknown champion id', async () => {
    await expect(resolveDevApiMock('/champions/999999', {})).rejects.toMatchObject({ statusCode: 404 })
  })

  it('404s on an unknown player', async () => {
    await expect(resolveDevApiMock('/truemains/no-such-player-xyz/profile', {}))
      .rejects.toMatchObject({ statusCode: 404 })
  })

  it('404s (not a URIError/500) on a malformed player segment', async () => {
    await expect(resolveDevApiMock('/truemains/foo%2/profile', {}))
      .rejects.toMatchObject({ statusCode: 404 })
  })
})
