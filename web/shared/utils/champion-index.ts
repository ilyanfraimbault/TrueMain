import type { ChampionTierListResponse } from '../types/champions'
import type {
  ChampionIndexLink,
  ChampionIndexResponse,
  ChampionIndexTierGroup,
} from '../types/champion-index'
import type { ChampionStaticListItem } from '../types/static-data'

/**
 * Assembles the server-rendered champion link graph (#1209) out of the two
 * sources that already exist: the DDragon champion list (names) and the
 * backend tier list (ranking).
 *
 * Pure and shared so the endpoint that serves it and the tests that pin its
 * rules are looking at the same function — the handler around it does nothing
 * but fetch, cache and hand the payloads over.
 *
 * Every upstream degrades on its own, the same contract as
 * `resolveChampionBuildSummary`: no DDragon means no names and therefore no
 * links (an anchor labelled `Champion 103` is worse than no anchor), no backend
 * means no tiers, and neither substitutes for the other.
 */

const EMPTY: ChampionIndexResponse = { patch: null, champions: [], tiers: [] }

export function emptyChampionIndex(): ChampionIndexResponse {
  return { ...EMPTY }
}

function nameById(staticList: ChampionStaticListItem[] | null | undefined): Map<number, string> {
  const names = new Map<number, string>()
  for (const item of staticList ?? []) {
    // A blank name is the same failure as a missing one — it would render an
    // empty anchor, which is a link with no anchor text at all.
    if (item.name) names.set(item.championId, item.name)
  }
  return names
}

/**
 * Every live champion, A→Z by display name.
 *
 * `localeCompare` with an explicit `en` locale, not the ambient one: this list
 * is rendered server-side and the server's locale is not the reader's, so an
 * implicit collation would make the SSR order and a client-side re-render
 * disagree — a hydration mismatch for the sake of where `Kai'Sa` sorts.
 */
export function championIndexLinks(
  staticList: ChampionStaticListItem[] | null | undefined,
): ChampionIndexLink[] {
  const links: ChampionIndexLink[] = []
  for (const [championId, name] of nameById(staticList)) {
    links.push({ championId, name })
  }
  return links.sort((a, b) => a.name.localeCompare(b.name, 'en'))
}

/**
 * The tier list, as named links, strongest tier first and strongest-first
 * within a tier (the order the backend already returns).
 *
 * **A champion appears once**, under its strongest tier, on the lane that
 * earned it. The backend tiers per (champion, lane), so a flex pick holds two
 * rows — and this block is a *summary* of the table above it, the same way
 * #1123's paragraph describes `builds[0]` rather than every build. Listing
 * `Ahri` twice would also split the anchor text pointing at one page across two
 * links, which is the opposite of what the block exists to do.
 *
 * `limit` caps the total number of entries across all groups (the homepage
 * block wants the strongest dozen, not 173 links on the site's front door).
 * Groups left empty by the cap are dropped rather than rendered as a bare tier
 * letter.
 */
export function championIndexTiers(
  tierList: ChampionTierListResponse | null | undefined,
  staticList: ChampionStaticListItem[] | null | undefined,
  limit?: number | null,
): ChampionIndexTierGroup[] {
  const names = nameById(staticList)
  const seen = new Set<number>()
  const groups: ChampionIndexTierGroup[] = []
  // `null`/`undefined` means "no cap"; a non-positive cap means "nothing", which
  // is what a caller asking for 0 entries asked for.
  let remaining = limit == null ? Number.POSITIVE_INFINITY : limit

  for (const group of tierList?.tiers ?? []) {
    if (remaining <= 0) break
    const entries: ChampionIndexTierGroup['entries'] = []
    for (const entry of group.entries) {
      if (remaining <= 0) break
      const name = names.get(entry.championId)
      if (!name || seen.has(entry.championId)) continue
      seen.add(entry.championId)
      entries.push({ championId: entry.championId, name, position: entry.position })
      remaining--
    }
    if (entries.length > 0) groups.push({ tier: group.tier, entries })
  }

  return groups
}

/**
 * The patch the tier groups describe. Deliberately read off the *backend*
 * response and never off DDragon: the block prints this next to the ranking, so
 * it has to be the patch the ranking was computed for. Empty string → null, so
 * the caller renders no patch rather than a blank one.
 */
export function championIndexPatch(
  tierList: ChampionTierListResponse | null | undefined,
): string | null {
  return tierList?.patchVersion || null
}
