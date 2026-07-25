import { beforeAll, describe, expect, it, vi } from 'vitest'
import { pageParams, resolveDevApiMock, tierFor } from '~~/server/utils/dev-api-mock'
import type { ChampionMainsComparison } from '~~/shared/types/champions'

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

// The dev mock exists so the frontend can be verified without a backend, so a
// branch that contradicts the real contract is worse than no mock at all — it
// makes a wrong frontend look correct locally. These pin the mains-comparison
// payload (#528) against ChampionMainsComparisonResponse.
describe('resolveDevApiMock: /champions/{id}/mains-comparison', () => {
  const path = '/champions/157/mains-comparison'

  async function comparison(query: Record<string, string>) {
    return await resolveDevApiMock(path, query) as ChampionMainsComparison
  }

  it('400s on a malformed Riot ID, rather than reporting it as unknown', async () => {
    // The controller validates well-formedness before the service runs, so a
    // typo must not surface as "we don't track this account yet".
    await expect(comparison({ account: 'NoSeparator' })).rejects.toMatchObject({ statusCode: 400 })
    await expect(comparison({ account: '#OnlyTag' })).rejects.toMatchObject({ statusCode: 400 })
    await expect(comparison({ account: 'OnlyName#' })).rejects.toMatchObject({ statusCode: 400 })
    await expect(comparison({})).rejects.toMatchObject({ statusCode: 400 })
    await expect(comparison({ account: 'Sheiden#1234', main: 'NoSeparator' }))
      .rejects.toMatchObject({ statusCode: 400 })
  })

  it('accepts the Name-TAG slug form the API also accepts', async () => {
    // Deliberately looser than the app's own parseRiotId, which requires '#'.
    const res = await comparison({ account: 'Sheiden-1234' })
    expect(res.status).toBe('OK')
  })

  it('reports a well-formed but unheld Riot ID as UNKNOWN_ACCOUNT with no columns', async () => {
    const res = await comparison({ account: 'Nobody#KR1' })
    expect(res.status).toBe('UNKNOWN_ACCOUNT')
    expect(res.player).toBeNull()
    expect(res.mains).toBeNull()
  })

  it('keeps the player column when only the target is unknown', async () => {
    // Mirrors ChampionMainsComparisonQueryService: the compared account
    // resolved, so only the yardstick is missing.
    const res = await comparison({ account: 'Sheiden#1234', main: 'Ghost#KR1' })
    expect(res.status).toBe('UNKNOWN_TARGET')
    expect(res.mains).toBeNull()
    expect(res.player).not.toBeNull()
    expect(res.player!.identity?.gameName).toBe('Sheiden')
    expect(res.player!.games).toBeGreaterThan(0)
  })

  it('derives winRate, sampleMet and status instead of hardcoding them', async () => {
    const res = await comparison({ account: 'Sheiden#1234' })
    for (const side of [res.player!, res.mains!]) {
      expect(side.winRate).toBeCloseTo(side.wins / side.games, 2)
      expect(side.wins).toBeLessThanOrEqual(side.games)
      expect(side.sampleMet).toBe(side.games >= res.minGames)
    }
    expect(res.status).toBe(
      res.player!.sampleMet && res.mains!.sampleMet ? 'OK' : 'INSUFFICIENT_SAMPLE',
    )
    // The pool column has no single owner; a targeted one does.
    expect(res.mains!.identity).toBeNull()
    expect(res.mains!.players).toBeGreaterThan(1)
  })

  it('echoes the normalised patch and position it was scoped to', async () => {
    const res = await comparison({ account: 'Sheiden#1234', position: 'MIDDLE', patch: '16.4.521.123' })
    expect(res.patch).toBe('16.4')
    expect(res.position).toBe('MIDDLE')

    const unpinned = await comparison({ account: 'Sheiden#1234' })
    expect(unpinned.patch).toBeNull()
    expect(unpinned.position).toBeNull()
  })
})
