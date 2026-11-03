import { randomUUID } from 'node:crypto'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { createDiskCache, type DiskCache } from './disk-cache'

/**
 * The rendered HTML of the pages everyone reads, shared by the workers (#2425). A render costs
 * 100–450 ms of a worker's only thread, for a page that is the same for every visitor: nothing in
 * these pages' server render reads a cookie or a visitor's header.
 *
 * Fresh for a minute; for an hour after that it is still served at once, while a background render
 * replaces it, so a visitor never waits for a render a previous one has already paid for. A render
 * with a failed fetch is never stored (`plugins/html-cache-guard.server.ts`): the failure belongs to
 * that moment, not to the page.
 */
export const HTML_FRESH_MS = 60 * 1000
export const HTML_STALE_MS = 60 * 60 * 1000

/** Set on a request that must render: the warm-up, and the background render of a stale page. */
export const HTML_RENDER_HEADER = 'x-truemain-render'

// Only ever sent to this worker by itself, so it does not have to be known to the others.
export const HTML_RENDER_KEY = randomUUID()

// Not on a volume: a page names the build's own script files, so it must not outlive its build.
const HTML_MAX_BYTES = 256 * 1024 * 1024

const CACHEABLE_PATHS = [/^\/$/, /^\/champions$/, /^\/champions\/tierlist$/, /^\/champions\/[a-z0-9-]+$/i, /^\/truemains$/]

// What these pages read from their URL. Anything else varies nothing on the page, so a request
// carrying it renders without being stored: a crawler's junk parameters cannot fill the budget.
const CACHEABLE_PARAMS = new Set(['elo', 'everyone', 'order', 'otpOnly', 'page', 'patch', 'position', 'region', 'sort', 'vs', 'championId'])

/** The cache key of a request's path and query, or null when it does not go through the cache. */
export function htmlCacheKey(requestPath: string): string | null {
  const [path = '', query] = requestPath.split('?', 2)
  if (!CACHEABLE_PATHS.some(pattern => pattern.test(path))) return null
  if (!query) return path

  const params = new URLSearchParams(query)
  for (const name of params.keys()) {
    if (!CACHEABLE_PARAMS.has(name)) return null
  }
  params.sort()
  return `${path}?${params.toString()}`
}

export type HtmlPageState = 'fresh' | 'stale' | 'expired'

export function htmlPageState(storedAt: number, now: number): HtmlPageState {
  const age = now - storedAt
  if (age <= HTML_FRESH_MS) return 'fresh'
  return age <= HTML_STALE_MS ? 'stale' : 'expired'
}

export interface HtmlHeader {
  storedAt: number
  headers: Record<string, string>
}

/**
 * The host this site answers on, when configured. A render this process asks for itself has no
 * visitor to take a host from, and a page prints its own address (the share links).
 */
export function siteHost(): string | null {
  const url = process.env.NUXT_PUBLIC_SITE_URL
  return url ? new URL(url).host : null
}

/** What a render this process asks for itself carries: past the cache, for the site's host. */
export function renderRequestHeaders(): Record<string, string> {
  const host = siteHost()
  return { [HTML_RENDER_HEADER]: HTML_RENDER_KEY, ...(host ? { host } : {}) }
}

/**
 * The headers worth serving a stored page with: what the render set, the SEO ones among them.
 * Null when the page must not be shared at all, because it set a cookie for one visitor.
 */
export function storableHeaders(headers: Record<string, unknown>): Record<string, string> | null {
  const stored: Record<string, string> = {}
  for (const [rawName, value] of Object.entries(headers)) {
    const name = rawName.toLowerCase()
    if (name === 'set-cookie') return null
    if (name === 'content-length' || name === 'etag' || name === 'x-powered-by') continue
    if (typeof value === 'string') stored[name] = value
  }
  return stored
}

let store: DiskCache<HtmlHeader> | undefined

export function getHtmlStore(): DiskCache<HtmlHeader> {
  store ??= createDiskCache<HtmlHeader>({
    dir: join(tmpdir(), 'truemain-html-cache'),
    maxBytes: HTML_MAX_BYTES,
    maxAgeMs: HTML_STALE_MS,
  })
  return store
}
