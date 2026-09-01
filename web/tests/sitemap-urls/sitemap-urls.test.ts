import type { SitemapUrl } from '#sitemap/types'
import { beforeEach, describe, expect, it, vi } from 'vitest'

/**
 * Cover for what the sitemap source advertises — and, as much, for what it must
 * not.
 *
 * #1334: the leaderboard walk asked for a page size the API rejects, so the
 * rendered sitemap silently carried no player profile at all while still
 * validating. #1337 then settled the question that bug had hidden: profiles do
 * not belong in the sitemap, because `/truemains/{nameTag}` fetches client-only
 * (#862) and its server-rendered document is a skeleton — the same generic
 * description on 5,000 pages. The family is gone, and the test below is what
 * stops it drifting back in: a future "the sitemap should list players" change
 * has to come through this file, and through the decision recorded next to it.
 *
 * The route is a Nitro handler written against auto-imported globals. Seeding
 * `$fetch` and `defineCachedFunction` before the module is evaluated stands in
 * for what Nitro injects at build time; the cache stub also hands us the inner
 * loader, so each test drives the fan-out directly instead of going through the
 * event handler and its response headers.
 */

let handleFetch: (path: string) => Promise<unknown>
let loadSitemapUrls: () => Promise<SitemapUrl[]>
const requested: string[] = []

Object.assign(globalThis, {
  $fetch: (path: string) => {
    requested.push(path)
    return handleFetch(path)
  },
  defineCachedFunction: (fn: () => Promise<SitemapUrl[]>) => {
    loadSitemapUrls = fn
    return fn
  },
})

await import('~~/server/routes/__sitemap__/urls')

const SLUGS = { 1: 'annie', 103: 'ahri', 11: 'masteryi' }

function locs(urls: SitemapUrl[]) {
  return urls.map(url => url.loc)
}

describe('sitemap urls', () => {
  beforeEach(() => {
    requested.length = 0
    handleFetch = () => Promise.resolve(SLUGS)
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

  it('never advertises a player profile', async () => {
    const urls = locs(await loadSitemapUrls())

    // The guard for #1337: profile pages render client-only, so their SSR
    // document is an empty skeleton — 5,000 near-duplicates is not something to
    // hand a crawler. `/truemains` itself is a real static page and is picked up
    // by route discovery, not from here.
    expect(urls.some(loc => loc.startsWith('/truemains/'))).toBe(false)
    expect(requested).not.toContain('/api/truemains')
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
