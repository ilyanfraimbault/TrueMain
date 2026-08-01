/**
 * Patch-scoped retention for the `/_ipx/**` byte cache.
 *
 * Every source URL the route serves is pinned to a patch — `/cdn/16.15.1/…`
 * on Data Dragon, `/16.15/…` on Community Dragon — so a patch release turns
 * the entire catalogue over at once: the previous patch's bytes are suddenly
 * unreachable but still occupy the 64 MB budget, and they only leave once
 * enough new traffic has pushed them down the LRU. That is exactly the moment
 * the cache is cold and needs its whole budget, so the old patch is swept
 * instead of being waited out.
 *
 * Retention is the current patch plus the two before it. Older patches stay
 * reachable through the champion page's patch filter, so they are not refused
 * — they are cached and served normally, and only swept the next time a newer
 * patch shows up. Evaluating expiry on every write instead would make browsing
 * an old patch permanently uncached: each icon would be stored and then
 * dropped by the following request.
 *
 * The window is the three newest patches *observed*, not `newest - 2`
 * arithmetic, so a season rollover (16.1 following 15.24) keeps the right
 * three rather than treating 15.24 as a thousand patches old.
 */

/** Current patch + the two before it. */
export const PATCHES_RETAINED = 3

const DDRAGON_PATCH = /\/cdn\/(\d+)\.(\d+)(?:\.\d+)?\//
const COMMUNITY_DRAGON_PATCH = /raw\.communitydragon\.org\/(\d+)\.(\d+)\//

/**
 * Comparable rank for the patch a cache key points at, or `null` when the key
 * carries no patch — the bundled `positions/` icons and Community Dragon's
 * `latest/` rank crests, both small fixed sets that no patch ever invalidates.
 *
 * Data Dragon's third segment is a hotfix number, not a patch, so `16.15.1` and
 * Community Dragon's `16.15` deliberately rank the same: one patch, two CDNs.
 */
export function parsePatchRank(key: string): number | null {
  const match = DDRAGON_PATCH.exec(key) ?? COMMUNITY_DRAGON_PATCH.exec(key)
  if (!match) return null

  const major = Number(match[1])
  const minor = Number(match[2])
  if (!Number.isFinite(major) || !Number.isFinite(minor)) return null

  return major * 1000 + minor
}

export interface PatchRetention {
  /**
   * Records the patch on `key`. Returns the set of patch ranks worth keeping
   * when this key introduced a patch newer than any seen so far — the caller
   * sweeps with it — and `null` the rest of the time, which is every request
   * but the first of a new patch.
   */
  observe: (key: string) => Set<number> | null
}

export function createPatchRetention(): PatchRetention {
  const seen = new Set<number>()
  let newest = Number.NEGATIVE_INFINITY

  return {
    observe(key) {
      const rank = parsePatchRank(key)
      if (rank === null) return null

      seen.add(rank)
      if (rank <= newest) return null

      newest = rank
      return new Set([...seen].sort((a, b) => b - a).slice(0, PATCHES_RETAINED))
    },
  }
}

/**
 * Predicate for {@link BoundedByteCache.purge}: true for keys pinned to a
 * patch outside `retained`. Keys with no patch always survive.
 */
export function isOutsideRetention(key: string, retained: Set<number>): boolean {
  const rank = parsePatchRank(key)
  return rank !== null && !retained.has(rank)
}
