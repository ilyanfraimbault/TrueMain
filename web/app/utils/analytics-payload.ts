/**
 * What the Umami tracker may send about where a visitor comes from (#2092) —
 * the data-minimisation measures of the CNIL's self-assessment grid for
 * consent-exempt audience measurement that the tracker does not apply itself:
 *
 * - **Campaign ids are not collected.** Umami's collector parses `utm_*` and
 *   ad click ids out of the page URL into columns of their own, and keeps the
 *   whole query string besides. They are removed from the URL before it leaves
 *   the browser. The rest of the query stays: it carries the page's filters
 *   (patch, elo, lane, matchup), which is what content analysis reads — so the
 *   tracker's `data-exclude-search`, which drops the whole query, is not used.
 * - **The referrer is cut down to its domain.** An external referrer is sent as
 *   its origin alone (`https://www.google.com/`), so no path or query is
 *   stored. A referrer on this site is dropped: the tracker sends it as a bare
 *   path (the previous page, on a reload and after every client-side
 *   navigation), and the collector stores that path even though it leaves the
 *   referrer domain empty — one visitor's pages chained together.
 *
 * The analytics database applies the same rules to anything that still reaches
 * it (the `umami-replay-cleanup` sidecar, `docs/ci.md`); keep the two lists of
 * campaign parameters in step.
 */

/** Ad click ids Umami stores (`gclid` … `twclid`) and the common others. */
const CLICK_ID_PARAMS = new Set([
  'gclid', 'gbraid', 'wbraid', 'dclid', 'fbclid', 'msclkid', 'ttclid', 'li_fat_id', 'twclid', 'igshid', 'yclid',
  'mc_cid', 'mc_eid',
])

function decodeParamName(raw: string): string {
  try {
    return decodeURIComponent(raw.replaceAll('+', ' ')).toLowerCase()
  }
  catch {
    return raw.toLowerCase()
  }
}

export function isCampaignParam(name: string): boolean {
  const key = decodeParamName(name)
  return key.startsWith('utm_') || CLICK_ID_PARAMS.has(key)
}

/**
 * The URL without its campaign parameters. Works on the string so the other
 * parameters keep their exact encoding and order, and on absolute and relative
 * URLs alike.
 */
export function stripCampaignParams(url: string): string {
  const hashAt = url.indexOf('#')
  const beforeHash = hashAt === -1 ? url : url.slice(0, hashAt)
  const hash = hashAt === -1 ? '' : url.slice(hashAt)
  const queryAt = beforeHash.indexOf('?')
  if (queryAt === -1) return url

  const kept = beforeHash
    .slice(queryAt + 1)
    .split('&')
    .filter(segment => segment !== '' && !isCampaignParam(segment.split('=', 1)[0]!))
  const base = beforeHash.slice(0, queryAt)
  const query = kept.length ? `?${kept.join('&')}` : ''
  return base + query + hash
}

/**
 * The referrer as the collector may store it: the origin of an external page,
 * or nothing for this site's own pages — a bare path, or a full URL on this
 * host — and anything unparseable.
 */
export function referrerDomainOnly(referrer: string, siteHostname: string): string {
  if (!referrer) return ''
  let parsed: URL
  try {
    parsed = new URL(referrer)
  }
  catch {
    return ''
  }
  if (!parsed.host || parsed.hostname === siteHostname) return ''
  return `${parsed.protocol}//${parsed.host}/`
}

/** A tracker payload with the two rules above applied; other fields pass through. */
export function minimizeAnalyticsPayload<T extends Record<string, unknown>>(payload: T, siteHostname: string): T {
  const next: Record<string, unknown> = { ...payload }
  if (typeof next.url === 'string') next.url = stripCampaignParams(next.url)
  if (typeof next.referrer === 'string') next.referrer = referrerDomainOnly(next.referrer, siteHostname)
  return next as T
}
