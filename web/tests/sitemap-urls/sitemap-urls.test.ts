import type { SitemapUrl } from '#sitemap/types'
import { beforeEach, describe, expect, it, vi } from 'vitest'

/**
 * Regression cover for #1334: the sitemap advertised 181 URLs in production —
 * every champion, every static page, and not one of the ~45k player profiles.
 * `truemainUrls()` walked the leaderboard with `pageSize=100`, the API caps
 * `PageSize` at 50 and rejects anything larger with a 400, and the walk's
 * `catch { break }` turned that into an empty list. The sitemap still rendered,
 * still validated, and silently lost a whole route family.
 *
 * The invariant is therefore about the *request*, not the payload: the walk has
 * to ask for a page the API would actually serve. `leaderboard()` below models
 * the server-side contract (`TruemainsLeaderboardQueryService.MaxPageSize` and
 * the controller's `[Range]`), so a page size the real API would reject fails
 * here the same way it failed in production.
 *
 * The route is a Nitro handler written against auto-imported globals. Seeding
 * `$fetch` and `defineCachedFunction` before the module is evaluated stands in
 * for what Nitro injects at build time; the cache stub also hands us the inner
 * loader, so each test drives the fan-out directly instead of going through the
 * event handler and its response headers.
 */

/** The API's real cap — see backend/Api/Services/Truemains/TruemainsLeaderboardQueryService.cs. */
const API_MAX_PAGE_SIZE = 50

interface Query { page: number, pageSize: number }

let handleFetch: (path: string, opts?: { query?: Query }) => Promise<unknown>
let loadSitemapUrls: () => Promise<SitemapUrl[]>

Object.assign(globalThis, {
  $fetch: (path: string, opts?: { query?: Query }) => handleFetch(path, opts),
  defineCachedFunction: (fn: () => Promise<SitemapUrl[]>) => {
    loadSitemapUrls = fn
    return fn
  },
})

await import('~~/server/routes/__sitemap__/urls')

const SLUGS = { 1: 'annie', 103: 'ahri' }

/**
 * A leaderboard the size of `total`, paginated like the real endpoint: it
 * validates `pageSize` first (400 above the cap, exactly as ASP.NET's `[Range]`
 * does), then serves `gameName`/`tagLine` pairs derived from the row index.
 */
function leaderboard(total: number) {
  return (_path: string, opts?: { query?: Query }) => {
    const { page, pageSize } = opts!.query!
    if (pageSize < 1 || pageSize > API_MAX_PAGE_SIZE) {
      throw new Error(`400: The field PageSize must be between 1 and ${API_MAX_PAGE_SIZE}.`)
    }
    const start = (page - 1) * pageSize
    const rows = Array.from(
      { length: Math.max(0, Math.min(pageSize, total - start)) },
      // Row 0 carries a Riot ID with a space and a non-ASCII letter — the shape
      // that exposed the double-encoding half of #1334.
      (_, i) => ({
        identity: start + i === 0
          ? { gameName: 'Álec Lightwood', tagLine: 'Jace' }
          : { gameName: `Player${start + i}`, tagLine: 'EUW' },
      }),
    )
    return Promise.resolve({ rows, page, pageSize, total })
  }
}

/** Routes the two upstreams the sitemap fans out to. */
function api(leaderboardTotal: number) {
  const ladder = leaderboard(leaderboardTotal)
  return (path: string, opts?: { query?: Query }) =>
    path === '/api/static/champion-slugs' ? Promise.resolve(SLUGS) : ladder(path, opts)
}

function locs(urls: SitemapUrl[]) {
  return urls.map(url => url.loc)
}

describe('sitemap urls', () => {
  beforeEach(() => {
    handleFetch = api(0)
  })

  it('asks for a page size the API accepts', async () => {
    const sizes: number[] = []
    const ladder = api(500)
    handleFetch = (path, opts) => {
      if (opts?.query) {
        sizes.push(opts.query.pageSize)
      }
      return ladder(path, opts)
    }

    await loadSitemapUrls()

    expect(sizes.length).toBeGreaterThan(0)
    for (const size of sizes) {
      expect(size).toBeLessThanOrEqual(API_MAX_PAGE_SIZE)
    }
  })

  it('advertises one profile URL per leaderboard row', async () => {
    handleFetch = api(120)

    const urls = locs(await loadSitemapUrls())

    // The bug shipped as an empty profile list, so the count is the assertion
    // that matters: 120 rows means the walk paged past the first response.
    expect(urls.filter(loc => loc.startsWith('/truemains/'))).toHaveLength(120)
    expect(urls).toContain('/truemains/Player119-EUW')
  })

  it('leaves the slug unencoded for the sitemap module to encode', async () => {
    handleFetch = api(1)

    const urls = locs(await loadSitemapUrls())

    // @nuxtjs/sitemap percent-encodes every `loc` itself. Pre-encoding here —
    // the way the app's own `to`/`href` builders correctly do — is applied
    // twice, and `%2520` reaches the `[nameTag]` route as the literal text
    // `%20`, which the backend cannot resolve (404).
    expect(urls).toContain('/truemains/Álec Lightwood-Jace')
    expect(urls.some(loc => loc.includes('%'))).toBe(false)
  })

  it('still advertises the champion pages alongside the profiles', async () => {
    handleFetch = api(10)

    const urls = locs(await loadSitemapUrls())

    expect(urls).toContain('/champions/annie')
    expect(urls).toContain('/champions/ahri')
  })

  it('stops at the documented ceiling instead of walking the whole ladder', async () => {
    // 45k rows is the production order of magnitude; the walk advertises the
    // top of the ladder and leaves the tail out on purpose.
    handleFetch = api(45_000)

    const urls = locs(await loadSitemapUrls())

    expect(urls.filter(loc => loc.startsWith('/truemains/'))).toHaveLength(5_000)
  })

  it('warns when the leaderboard contributes nothing', async () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {})
    handleFetch = (path, opts) => {
      if (path === '/api/static/champion-slugs') {
        return Promise.resolve(SLUGS)
      }
      return Promise.reject(new Error('502'))
    }

    const urls = locs(await loadSitemapUrls())

    // Degrading to the champion pages is deliberate; doing it silently is what
    // let #1334 sit in production unnoticed.
    expect(urls).toContain('/champions/annie')
    expect(warn).toHaveBeenCalledWith('[sitemap] leaderboard walk contributed no profile URLs')
    warn.mockRestore()
  })
})
