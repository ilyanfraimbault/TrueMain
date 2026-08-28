/**
 * `web/` and `admin/` are deliberately separate apps with no shared package, but
 * both read the same Data Dragon endpoints and must agree on what a champion id
 * and a patch string are. `normalizeDataDragonPatch`, `PATCH_PATTERN`,
 * `ALTERNATE_MODE_CHAMPION_ID_FLOOR` and `isLiveChampionId` below are therefore
 * a **synchronised copy** of `web/shared/utils/ddragon.ts`: change one, change
 * the twin, and keep them in the same order so a plain diff of the two files
 * stays readable. The pair drifted apart in both directions once already
 * (#1226) — each app ended up carrying a fix the other was missing.
 *
 * The web copy carries extra, web-only helpers around these.
 */

export function normalizeDataDragonPatch(patch?: string | null): string | null {
  if (!patch) {
    return null
  }

  const segments = patch.split('.').filter(Boolean)
  if (segments.length === 2) {
    return `${segments[0]}.${segments[1]}.1`
  }

  return patch
}

/**
 * A Data Dragon patch is `major.minor.patch` (e.g. `16.5.1`).
 *
 * `normalizeDataDragonPatch` above is a shape fixer, not a guard: it expands the
 * short `16.5` form the backend scopes expose and passes anything else through
 * untouched. Callers supply `?patch=` and the static endpoints interpolate the
 * result into a CDN URL *and* use it as a cache key, so an unvalidated value is
 * both a path-injection vector and an unbounded-cache-key vector — every
 * distinct string would pin its own entry for the payload TTL.
 */
export const PATCH_PATTERN = /^\d+\.\d+\.\d+$/

/**
 * Data Dragon lists one entry per playable champion *kit*, not per champion.
 * Patch 16.15 ("League classique") added 60 legacy kits reusing the original
 * champion's display name — `Jade_Ahri` with key `60103` next to Ahri's `103`.
 * The ingestor only aggregates queue 420, so those ids never carry a single
 * stat: left in, they duplicate every champion row with a dead end, and on the
 * public site they doubled every search hit and put 60 empty pages in the
 * sitemap (#966).
 *
 * Real Riot champion keys are still well under this floor (Naafiri, the
 * highest, is 950) and every alternate-mode kit so far sits at
 * `60000 + <base key>`, so cutting here keeps an order of magnitude of headroom
 * for real champions while catching a future mode built the same way.
 */
export const ALTERNATE_MODE_CHAMPION_ID_FLOOR = 10_000

export function isLiveChampionId(championId: number): boolean {
  return Number.isFinite(championId) && championId > 0 && championId < ALTERNATE_MODE_CHAMPION_ID_FLOOR
}
