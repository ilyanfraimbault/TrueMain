import { championPath } from '#site-shared/utils/champion-slug'
import { truemainNameTag, truemainProfilePath } from '#site-shared/utils/truemain-path'
import type { ChampionSlugMap } from '#site-shared/types/static-data'

/**
 * Links from the admin to the public site (#2291). The paths come from the site's
 * own builders (`web/shared/utils`), so an admin link never disagrees with the page
 * it points at; only the origin is the admin's, from `runtimeConfig.public.siteUrl`.
 */

/**
 * The absolute URL of `path` on the public site, or null when no site is configured
 * (local dev without `NUXT_PUBLIC_SITE_URL`) or there is no path to open — the
 * button is hidden then rather than pointing at the admin's own origin.
 */
export function siteHref(baseUrl: string | null | undefined, path: string | null | undefined): string | null {
  const base = baseUrl?.trim().replace(/\/+$/, '')
  if (!base || !path) {
    return null
  }
  return `${base}${path.startsWith('/') ? path : `/${path}`}`
}

/** A known player's page, `/players/{slug}`. */
export function knownPlayerPath(slug: string): string {
  return `/players/${encodeURIComponent(slug)}`
}

/**
 * A player's profile, `/truemains/{Name-TAG}`. Null without both halves of the Riot
 * ID: an account the pipeline has not resolved yet has no page to open.
 */
export function truemainPath(gameName: string | null | undefined, tagLine: string | null | undefined): string | null {
  if (!gameName?.trim() || !tagLine?.trim()) {
    return null
  }
  return truemainProfilePath(truemainNameTag(gameName, tagLine))
}

/**
 * A champion's page, `/champions/{slug}`. Falls back to the numeric id while the
 * slug map is missing, exactly like the site's own links: the site redirects it.
 */
export function championSitePath(championId: number, slugs: ChampionSlugMap | null | undefined): string {
  return championPath(championId, slugs)
}
