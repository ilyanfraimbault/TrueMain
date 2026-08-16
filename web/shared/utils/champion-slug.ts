import type { ChampionSlugMap } from '../types/static-data'

/**
 * Champion URL slugs (#1124): building a link, and reading one back.
 *
 * Pure and shared so the three places that must agree on a champion's URL — the
 * router (`pages/champions/[slug].vue`), every link builder, and the sitemap —
 * cannot drift. A mismatch here is not a broken page: it is a page that renders
 * fine under a URL the sitemap never advertises, or an internal link that
 * redirects on every click.
 */

/** Reverse of {@link ChampionSlugMap}: `slug → championId`. */
export type ChampionIdBySlug = ReadonlyMap<string, number>

export function buildChampionIdBySlug(slugs: ChampionSlugMap | null | undefined): ChampionIdBySlug {
  const index = new Map<string, number>()
  if (!slugs) return index
  for (const [id, slug] of Object.entries(slugs)) {
    const championId = Number(id)
    if (Number.isInteger(championId)) index.set(slug, championId)
  }
  return index
}

/**
 * The canonical path segment for a champion: its slug, or the numeric id when
 * the map hasn't loaded (or doesn't know the champion — a brand-new release
 * between DDragon updates). The fallback is deliberate: a numeric link still
 * reaches the page and gets redirected there, which is strictly better than
 * rendering a dead `/champions/undefined`.
 */
export function championSegment(
  championId: number,
  slugs: ChampionSlugMap | null | undefined,
): string {
  return slugs?.[String(championId)] ?? String(championId)
}

export function championPath(
  championId: number,
  slugs: ChampionSlugMap | null | undefined,
): string {
  return `/champions/${championSegment(championId, slugs)}`
}

export function truemainChampionPath(
  nameTag: string,
  championId: number,
  slugs: ChampionSlugMap | null | undefined,
): string {
  return `/truemains/${encodeURIComponent(nameTag)}/champions/${championSegment(championId, slugs)}`
}

/**
 * What a `/champions/{segment}` request should do. Split out of the middleware
 * so the decision — which drives a 404, a 503 and a 301 — is a pure function
 * with no Nuxt runtime around it, and so the cases that differ only in a
 * character (`Ahri`, `0103`) are directly testable.
 */
export type ChampionRouteAction =
  /** Canonical URL: render. */
  | { type: 'render', championId: number }
  /** Not this URL: 301 to `segment`, preserving query and hash. */
  | { type: 'redirect', championId: number, segment: string }
  /** No such champion: 404, permanently. */
  | { type: 'notFound' }
  /**
   * We cannot tell yet — the slug map never loaded, so a real slug and a typo
   * look identical. **503, never 404**: a 404 on `/champions/ahri` during a
   * DDragon outage with a cold cache would ask search engines to drop a
   * canonical, indexed URL over something transient and self-healing. This is
   * the case the plugin's "an outage only costs pretty URLs" note missed — that
   * is true of *link building*, which falls back to numeric ids, and false of
   * *route resolution*, which has nothing to fall back to.
   */
  | { type: 'unavailable' }

export function championRouteAction(
  segment: string,
  slugs: ChampionSlugMap | null | undefined,
  idBySlug: ChampionIdBySlug,
): ChampionRouteAction {
  const { championId, canonicalSegment } = resolveChampionParam(segment, slugs, idBySlug)

  if (championId === null) {
    // An empty map means we don't know the roster, not that this isn't part of
    // it. Numeric segments still resolve without the map, so only a *slug* can
    // land here for that reason.
    return idBySlug.size === 0 ? { type: 'unavailable' } : { type: 'notFound' }
  }

  return canonicalSegment !== null && canonicalSegment !== segment
    ? { type: 'redirect', championId, segment: canonicalSegment }
    : { type: 'render', championId }
}

export interface ResolvedChampionParam {
  /** Null when the segment names no champion we know — the page 404s. */
  championId: number | null
  /**
   * The segment this champion *should* be under. Null when unresolvable, and
   * equal to the input when the URL is already canonical.
   */
  canonicalSegment: string | null
}

/**
 * Reads a `/champions/{segment}` param back to a champion.
 *
 * Accepts three forms, and only one of them is canonical:
 *   - `ahri` — the slug. Canonical.
 *   - `103` — the legacy numeric id. Every link minted before #1124, plus any
 *     external backlink, is this shape; it resolves and the caller 301s.
 *   - `Ahri` — a slug in the wrong case, from a hand-typed or mangled URL.
 *     Resolved and redirected rather than 404'd, so case can never fork one
 *     page into two indexable URLs.
 */
export function resolveChampionParam(
  segment: string,
  slugs: ChampionSlugMap | null | undefined,
  idBySlug: ChampionIdBySlug,
): ResolvedChampionParam {
  const normalized = segment.trim().toLowerCase()

  const bySlug = idBySlug.get(normalized)
  if (bySlug !== undefined) {
    return { championId: bySlug, canonicalSegment: normalized }
  }

  // Numeric only after the slug lookup: no champion slug is all digits, so the
  // order is free, and checking the slug first means a future slug that *is*
  // numeric would still win over being read as an id.
  if (/^\d{1,7}$/.test(normalized)) {
    const championId = Number(normalized)
    if (championId > 0) {
      return {
        championId,
        // An id the slug map doesn't cover still has a canonical form: itself.
        // Returning null here would make the page redirect-loop on a champion
        // DDragon hasn't listed yet.
        canonicalSegment: championSegment(championId, slugs),
      }
    }
  }

  return { championId: null, canonicalSegment: null }
}
