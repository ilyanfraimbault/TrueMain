/** The site's URL of a known player's page (#2016), `/players/{slug}`. */
export function playerPath(slug: string): string {
  return `/players/${encodeURIComponent(slug)}`
}
