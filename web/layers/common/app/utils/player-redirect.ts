/**
 * Where `/players/{requested}` must send the visitor (#2253): a known player an
 * operator merged into another one answers under the kept player's slug, and the
 * page redirects there. Null when the page is already the right one or unknown.
 */
export function mergedPlayerPath(requested: string, persona: { slug: string } | null | undefined): string | null {
  if (!persona?.slug || persona.slug === requested.toLowerCase()) {
    return null
  }

  return `/players/${encodeURIComponent(persona.slug)}`
}
