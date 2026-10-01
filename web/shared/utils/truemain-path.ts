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
