// @vitest-environment node
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { claimSharedWarmup, markSharedWarm, markWorkerWarm } from '~~/server/utils/warmup-state'
import { warmWorker } from '~~/server/utils/warmup-worker'

vi.mock('~~/server/utils/warmup-state', () => ({
  claimSharedWarmup: vi.fn(),
  markSharedWarm: vi.fn(),
  markWorkerWarm: vi.fn(),
}))
vi.mock('~~/server/utils/ipx-disk-store', () => ({ getIpxDiskCache: () => null }))
vi.mock('~~/server/utils/html-cache', () => ({ getHtmlStore: () => ({ sweep: async () => 0 }) }))

const DRAGON = 'https://ddragon.leagueoflegends.com/cdn/16.21.1/img'

const READS: Record<string, unknown> = {
  '/api/static/versions': ['16.21.1', '16.20.1'],
  '/api/static/champions': [{ championId: 103, iconUrl: `${DRAGON}/champion/Ahri.png` }],
  '/api/static/champion-slugs': { 103: 'ahri', 99: 'lux' },
  '/api/static/items?patch=16.21': { 3078: { iconUrl: `${DRAGON}/item/3078.png` } },
}

describe('warming a worker', () => {
  const raw = vi.fn()
  const asked: string[] = []
  const localFetch = vi.fn(async (path: string, _headers?: Record<string, string>) => {
    asked.push(path)
    return new Response(JSON.stringify(READS[path] ?? {}))
  })

  vi.stubGlobal('$fetch', { raw })

  beforeEach(() => {
    asked.length = 0
    vi.mocked(markWorkerWarm).mockClear()
    vi.mocked(markSharedWarm).mockClear()
    vi.mocked(claimSharedWarmup).mockReset()
    raw.mockReset()
    raw.mockResolvedValue({ status: 200 })
    vi.mocked(claimSharedWarmup).mockReturnValue(true)
  })

  it('waits for the API before asking itself for anything', async () => {
    raw.mockResolvedValueOnce({ status: 503 }).mockRejectedValueOnce(new Error('refused'))

    await warmWorker(localFetch, 'http://api:8080', { apiPollMs: 0 })

    expect(raw).toHaveBeenCalledTimes(3)
    expect(raw).toHaveBeenCalledWith('http://api:8080/readyz', expect.anything())
  })

  it('reads the static data, renders the pages, and reads what every champion keeps in memory', async () => {
    await warmWorker(localFetch, 'http://api:8080')

    expect(asked).toEqual(expect.arrayContaining([
      '/api/static/champion-slugs',
      '/api/static/items?patch=16.21',
      '/api/static/rune-tree?patch=16.21.1',
      '/champions',
      '/champions/tierlist',
      '/champions/ahri',
      '/api/static/99',
      '/api/static/99?patch=16.21',
      '/api/champion-summary/103?eloBracket=MASTER_PLUS',
    ]))
    expect(asked.indexOf('/api/static/versions')).toBeLessThan(asked.indexOf('/api/static/items?patch=16.21'))
    expect(markWorkerWarm).toHaveBeenCalledOnce()
  })

  it('renders each page several times, for the optimiser', async () => {
    await warmWorker(localFetch, 'http://api:8080')

    expect(asked.filter(path => path === '/champions').length).toBeGreaterThan(1)
  })

  it('warms the images it collected when it wins the claim', async () => {
    await warmWorker(localFetch, 'http://api:8080')

    expect(asked).toEqual(expect.arrayContaining([
      `/_ipx/f_webp&s_64x64/${DRAGON}/item/3078.png`,
      `/_ipx/f_webp&s_64x64/${DRAGON}/champion/Ahri.png`,
      '/_ipx/w_1280&f_webp/https://ddragon.leagueoflegends.com/cdn/img/champion/splash/Ahri_0.jpg',
    ]))
    expect(markSharedWarm).toHaveBeenCalledOnce()
  })

  it('renders every champion page once when it wins the claim, each from its own address', async () => {
    await warmWorker(localFetch, 'http://api:8080')

    const lastRender = (path: string) => localFetch.mock.calls.findLast(call => call[0] === path)
    expect(lastRender('/champions/ahri')?.[1]).toEqual({ 'x-forwarded-for': expect.stringMatching(/^203\.0\.113\./) })
    expect(lastRender('/champions/lux')?.[1]).not.toEqual(lastRender('/champions/ahri')?.[1])
  })

  it('leaves the images to the worker that won the claim', async () => {
    vi.mocked(claimSharedWarmup).mockReturnValue(false)

    await warmWorker(localFetch, 'http://api:8080')

    expect(asked.filter(path => path.startsWith('/_ipx/'))).toEqual([])
    expect(localFetch.mock.calls.filter(call => call[1])).toEqual([])
    expect(markSharedWarm).not.toHaveBeenCalled()
  })

  it('carries on past a failed request, leaving it to the visitor who asks', async () => {
    const flaky = vi.fn(async (path: string) => {
      if (path === '/champions/ahri') throw new Error('/champions/ahri answered 500')
      return localFetch(path)
    })

    await warmWorker(flaky, 'http://api:8080')

    expect(markWorkerWarm).toHaveBeenCalledOnce()
    expect(asked).toContain('/api/static/99')
  })

  it('still warms what it can when the versions cannot be read', async () => {
    const noVersions = vi.fn(async (path: string) => new Response(path === '/api/static/versions' ? 'nope' : '{}'))

    await warmWorker(noVersions, 'http://api:8080')

    expect(markWorkerWarm).toHaveBeenCalledOnce()
    expect(noVersions).not.toHaveBeenCalledWith(expect.stringContaining('?patch='))
  })
})
