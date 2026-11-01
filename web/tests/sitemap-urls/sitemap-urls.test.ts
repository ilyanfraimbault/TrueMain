import type { SitemapUrl } from '#sitemap/types'
import { beforeEach, describe, expect, it, vi } from 'vitest'

/**
 * Cover for what the sitemap source advertises — and, as much, for what it must
 * not.
 *
 * #1334: the leaderboard walk asked for a page size the API rejects, so the
 * rendered sitemap silently carried no player profile at all while still
 * validating. #1337 then dropped profiles altogether, their server-rendered
 * document being a skeleton; #2359 server-renders the profile and brings back the
 * leaderboard's head only — the tests below pin both the head and its cap.
 *
 * The route is a Nitro handler written against auto-imported globals. Seeding
 * `$fetch` and `defineCachedFunction` before the module is evaluated stands in
 * for what Nitro injects at build time; the cache stub also hands us the inner
 * loader, so each test drives the fan-out directly instead of going through the
 * event handler and its response headers.
 */

let handleFetch: (path: string, query?: Record<string, number>) => Promise<unknown>
let loadSitemapUrls: () => Promise<SitemapUrl[]>
const requested: string[] = []

Object.assign(globalThis, {
  $fetch: (path: string, options?: { query?: Record<string, number> }) => {
    requested.push(path)
    return handleFetch(path, options?.query)
  },
  defineCachedFunction: (fn: () => Promise<SitemapUrl[]>) => {
    loadSitemapUrls = fn
    return fn
  },
})

const { SITEMAP_TRUEMAIN_PROFILES } = await import('~~/server/routes/__sitemap__/urls')

const SLUGS = { 1: 'annie', 103: 'ahri', 11: 'masteryi' }

/** The directory knows two of the three champions, Annie on two lanes. */
const DIRECTORY = [
  { championId: 1, lastUpdatedAtUtc: '2026-08-24T09:12:00Z' },
  { championId: 1, lastUpdatedAtUtc: '2026-08-26T22:41:00Z' },
  { championId: 103, lastUpdatedAtUtc: '2026-08-25T04:00:00Z' },
]

/** A leaderboard of `size` players, served 50 to a page. */
function leaderboard(size: number) {
  return (query?: Record<string, number>) => {
    const page = query?.page ?? 1
    const pageSize = query?.pageSize ?? 50
    const start = (page - 1) * pageSize
    const rows = Array.from({ length: Math.max(0, Math.min(pageSize, size - start)) }, (_, index) => ({
      identity: { gameName: `Player ${start + index + 1}`, tagLine: 'EUW' },
    }))
    return Promise.resolve({ rows, page, pageSize, total: size })
  }
}

/** Routes the upstreams the sitemap fans out to. */
function api(directory: unknown = DIRECTORY, players = leaderboard(0)) {
  return (path: string, query?: Record<string, number>) => {
    if (path === '/api/champions') return Promise.resolve(directory)
    if (path === '/api/truemains') return players(query)
    if (path === '/api/players') return Promise.resolve({ players: [] })
    return Promise.resolve(SLUGS)
  }
}

function locs(urls: SitemapUrl[]) {
  return urls.map(url => url.loc)
}

describe('sitemap urls', () => {
  beforeEach(() => {
    requested.length = 0
    handleFetch = api()
  })

  it('advertises one URL per champion slug', async () => {
    const urls = locs(await loadSitemapUrls())

    expect(urls).toEqual(['/champions/annie', '/champions/masteryi', '/champions/ahri'])
  })

  it('advertises the slug, never the numeric id', async () => {
    // A numeric `loc` would put every champion in the sitemap as a 301 to
    // somewhere else (#1124).
    const urls = locs(await loadSitemapUrls())

    expect(urls.some(loc => /\/champions\/\d+$/.test(loc))).toBe(false)
  })

  it('advertises the leaderboard\'s head, in its order and slug', async () => {
    handleFetch = api(DIRECTORY, leaderboard(3))
    const urls = locs(await loadSitemapUrls())

    expect(urls.filter(loc => loc.startsWith('/truemains/'))).toEqual([
      '/truemains/Player%201-EUW',
      '/truemains/Player%202-EUW',
      '/truemains/Player%203-EUW',
    ])
  })

  it('stops at the cap, never the whole leaderboard', async () => {
    // #2359 brings profiles back for their server-rendered content; the tail of
    // thinly sampled accounts is still what #1337 kept out.
    handleFetch = api(DIRECTORY, leaderboard(5000))
    const urls = locs(await loadSitemapUrls())

    expect(urls.filter(loc => loc.startsWith('/truemains/'))).toHaveLength(SITEMAP_TRUEMAIN_PROFILES)
    expect(urls).toContain(`/truemains/Player%20${SITEMAP_TRUEMAIN_PROFILES}-EUW`)
  })

  it('keeps the other families when the leaderboard fails, and says so', async () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {})
    const base = api()
    handleFetch = (path, query) => path === '/api/truemains' ? Promise.reject(new Error('502')) : base(path, query)

    const urls = locs(await loadSitemapUrls())

    expect(urls).toContain('/champions/annie')
    expect(urls.some(loc => loc.startsWith('/truemains/'))).toBe(false)
    expect(warn).toHaveBeenCalledWith('[sitemap] leaderboard source failed; truemain profiles left out')
    warn.mockRestore()
  })

  it('decorates a champion with the most recent lastmod across its lanes', async () => {
    const urls = await loadSitemapUrls()

    // Annie is folded on two lanes; the page shows both, so its freshness is
    // the later of the two (#1256).
    expect(urls).toContainEqual({ loc: '/champions/annie', lastmod: '2026-08-26' })
    expect(urls).toContainEqual({ loc: '/champions/ahri', lastmod: '2026-08-25' })
  })

  it('emits a champion the directory does not mention without a lastmod', async () => {
    const urls = await loadSitemapUrls()

    // The days after a patch flip, before a champion's lane is folded. An
    // absent date beats an invented one.
    expect(urls).toContainEqual({ loc: '/champions/masteryi' })
  })

  it('keeps every URL when the directory is down, losing only the dates', async () => {
    // The slug map decides *which* URLs exist; the directory only decorates
    // them, so its outage must not cost a single page.
    const base = api()
    handleFetch = (path, query) =>
      path === '/api/champions' ? Promise.reject(new Error('503')) : base(path, query)

    const urls = await loadSitemapUrls()

    expect(locs(urls)).toEqual(['/champions/annie', '/champions/masteryi', '/champions/ahri'])
    expect(urls.every(url => !('lastmod' in url))).toBe(true)
  })

  it('warns and degrades to the static pages when the champion source fails', async () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {})
    handleFetch = () => Promise.reject(new Error('502'))

    // Losing the dynamic entries must not fail the sitemap — the static pages
    // still ship — but it must not pass unnoticed either, which is exactly how
    // #1334 stayed in production.
    await expect(loadSitemapUrls()).resolves.toEqual([])
    expect(warn).toHaveBeenCalledWith('[sitemap] champion source contributed no URLs')
    warn.mockRestore()
  })
})
