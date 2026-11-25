// @vitest-environment node
import { afterEach, describe, expect, it, vi } from 'vitest'
import {
  HTML_FRESH_MS,
  HTML_RENDER_HEADER,
  HTML_RENDER_KEY,
  HTML_STALE_MS,
  htmlCacheKey,
  htmlPageState,
  renderRequestHeaders,
  siteHost,
  storableHeaders,
} from '~~/server/utils/html-cache'

describe('which requests go through the HTML cache', () => {
  it.each(['/', '/champions', '/champions/tierlist', '/champions/ahri', '/truemains'])('takes %s', (path) => {
    expect(htmlCacheKey(path)).toBe(path)
  })

  it.each(['/about', '/truemains/Faker-KR1', '/champions/ahri/extra', '/api/static/champions', '/_nuxt/a.js', '/_ipx/x'])(
    'leaves %s out',
    (path) => {
      expect(htmlCacheKey(path)).toBeNull()
    },
  )

  it('gives one entry to the same filters in any order', () => {
    expect(htmlCacheKey('/champions?position=TOP&elo=GOLD_PLUS'))
      .toBe(htmlCacheKey('/champions?elo=GOLD_PLUS&position=TOP'))
  })

  it('keeps filters apart', () => {
    expect(htmlCacheKey('/champions?elo=GOLD')).not.toBe(htmlCacheKey('/champions?elo=SILVER'))
    expect(htmlCacheKey('/champions?elo=GOLD')).not.toBe(htmlCacheKey('/champions'))
  })

  it('does not store a request carrying a parameter the page does not read', () => {
    expect(htmlCacheKey('/champions?utm_source=x')).toBeNull()
    expect(htmlCacheKey('/champions?elo=GOLD&junk=1')).toBeNull()
  })

  it('reads each page\'s own keys: a leaderboard filter means nothing on a champion page', () => {
    expect(htmlCacheKey('/truemains?region=korea')).toBe('/truemains?region=korea')
    expect(htmlCacheKey('/champions/ahri?region=korea')).toBeNull()
    expect(htmlCacheKey('/champions/ahri?vs=122')).toBe('/champions/ahri?vs=122')
  })
})

describe('how long a page lasts', () => {
  const now = 1_000_000_000

  it('is fresh for its first window', () => {
    expect(htmlPageState(now - HTML_FRESH_MS, now)).toBe('fresh')
  })

  it('is stale after that, and still served', () => {
    expect(htmlPageState(now - HTML_FRESH_MS - 1, now)).toBe('stale')
    expect(htmlPageState(now - HTML_STALE_MS, now)).toBe('stale')
  })

  it('has expired past the stale window', () => {
    expect(htmlPageState(now - HTML_STALE_MS - 1, now)).toBe('expired')
  })
})

describe('what a stored page keeps of its headers', () => {
  it('keeps what the render set, the SEO ones among them', () => {
    expect(storableHeaders({ 'Content-Type': 'text/html', 'X-Robots-Tag': 'index, follow' }))
      .toEqual({ 'content-type': 'text/html', 'x-robots-tag': 'index, follow' })
  })

  it('drops what describes one response', () => {
    expect(storableHeaders({ 'content-length': '10', 'etag': 'x', 'x-powered-by': 'Nuxt' })).toEqual({})
  })

  it('refuses a page that set a cookie', () => {
    expect(storableHeaders({ 'content-type': 'text/html', 'set-cookie': 'a=1' })).toBeNull()
  })
})

describe('a render this process asks for itself', () => {
  afterEach(() => {
    vi.unstubAllEnvs()
  })

  it('goes past the cache, as the edge would send it', () => {
    vi.stubEnv('NUXT_PUBLIC_SITE_URL', 'https://truemain.lol')

    expect(siteHost()).toBe('truemain.lol')
    expect(renderRequestHeaders()).toEqual({
      [HTML_RENDER_HEADER]: HTML_RENDER_KEY,
      'host': 'truemain.lol',
      'x-forwarded-proto': 'https',
    })
  })

  it('takes the protocol from the site, for a host without TLS', () => {
    vi.stubEnv('NUXT_PUBLIC_SITE_URL', 'http://203.0.113.7:3001')

    expect(renderRequestHeaders()).toMatchObject({ 'host': '203.0.113.7:3001', 'x-forwarded-proto': 'http' })
  })

  it('carries no host when none is configured', () => {
    vi.stubEnv('NUXT_PUBLIC_SITE_URL', '')

    expect(siteHost()).toBeNull()
    expect(renderRequestHeaders()).toEqual({ [HTML_RENDER_HEADER]: HTML_RENDER_KEY })
  })
})
