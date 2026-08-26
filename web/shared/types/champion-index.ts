/**
 * The site's server-rendered champion link graph (#1209).
 *
 * Every grid, table and tier list that holds a champion link is rendered
 * client-side (`server: false`, the #149 hydration fix), so the ~174
 * `/champions/{slug}` URLs existed for a crawler only inside `sitemap.xml`,
 * with **zero inbound links** — the textbook "Discovered – currently not
 * indexed" profile. These are the read models that put real anchors back into
 * the server-rendered HTML, resolved to *names* server-side exactly like
 * `champion-build-summary` (#1123): the pages that need them must not pay the
 * ~20 kB static champion list (names + CDN icon URLs) to print a list of words.
 */

/**
 * One champion, as an anchor.
 *
 * Carries the id, **not** the slug: `plugins/champion-slugs.ts` already loads
 * the `championId → slug` map into app-wide state before the first render, on
 * every page and during SSR, so the components below build their `href` with
 * `pathFor()` like every other link in the app. Putting a slug in this payload
 * would ship a second copy of a map already in the HTML — and a copy that could
 * disagree with the router.
 */
export interface ChampionIndexLink {
  championId: number
  name: string
}

/** A champion in a tier, on the lane it was tiered for. */
export interface ChampionIndexTierEntry extends ChampionIndexLink {
  /** Riot position key (`TOP`…`UTILITY`) — tiers are computed per lane. */
  position: string
}

export interface ChampionIndexTierGroup {
  /** `S` | `A` | `B` | `C` | `D`, strongest first. */
  tier: string
  entries: ChampionIndexTierEntry[]
}

export interface ChampionIndexResponse {
  /**
   * Patch the tier groups describe, or `null` when the backend had nothing to
   * say. Never the DDragon patch the names came from — the block prints it next
   * to measurements, so it has to be the patch those measurements are from.
   */
  patch: string | null
  /**
   * Every live champion, A→Z. Empty on the `tiers` view — the two are requested
   * separately so a page pays only for the list it renders.
   */
  champions: ChampionIndexLink[]
  /** Tier groups, strongest first. Empty on the `all` view. */
  tiers: ChampionIndexTierGroup[]
}
