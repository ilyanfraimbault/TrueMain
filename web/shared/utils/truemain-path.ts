/**
 * The one way to spell a truemain's URL (#1734). A Riot ID becomes the slug
 * `{gameName}-{tagLine}` (or the bare name when untagged — `-` is an
 * unambiguous separator because tag lines never contain one), and the slug is
 * URL-encoded into `/truemains/{slug}`. Every profile link, the favorites store
 * and the player-scoped champion pages build on these two, so a link never
 * disagrees with the page it points at.
 */
export function truemainNameTag(gameName: string, tagLine: string | null | undefined): string {
  const name = gameName.trim()
  const tag = tagLine?.trim()
  return tag ? `${name}-${tag}` : name
}

export function truemainProfilePath(nameTag: string): string {
  return `/truemains/${encodeURIComponent(nameTag)}`
}

/**
 * The reverse of {@link truemainNameTag}: splits a route slug back into its
 * Riot ID halves on the **last** `-`, so a game name may itself contain
 * hyphens (#948). Mirrors the backend's `NameTagParser.TryParse` exactly —
 * same separator, same rejection of an empty or blank half — so the page and
 * the API never disagree on who a slug names. `null` when the slug is not a
 * well-formed `{gameName}-{tagLine}`.
 */
export function parseTruemainNameTag(nameTag: string): { gameName: string, tagLine: string } | null {
  if (!nameTag.trim()) return null
  const idx = nameTag.lastIndexOf('-')
  if (idx <= 0 || idx === nameTag.length - 1) return null
  const gameName = nameTag.slice(0, idx)
  const tagLine = nameTag.slice(idx + 1)
  if (!gameName.trim() || !tagLine.trim()) return null
  return { gameName, tagLine }
}

/**
 * The label a truemain page shows before its profile fetch lands:
 * `gameName#tagLine` derived from the slug alone, or the raw slug when it does
 * not parse. Computed from the route only, so the server render and the
 * client's first render agree (the hydration trap of #862) and the SSR
 * `<title>` reads `Name#TAG` rather than `Name-TAG` (#948).
 */
export function truemainSlugLabel(nameTag: string): string {
  const parsed = parseTruemainNameTag(nameTag)
  return parsed ? `${parsed.gameName}#${parsed.tagLine}` : nameTag
}
