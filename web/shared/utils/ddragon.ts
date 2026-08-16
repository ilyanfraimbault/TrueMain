export function formatPercentage(value: number, digits = 1): string {
  return `${(value * 100).toFixed(digits)}%`
}

/**
 * Percentage of a stat the backend may not know, rendered as an em dash when it
 * is null. Ban rate (#920) is the first of these: it is null for every patch
 * older than ban ingestion, and `formatPercentage(null)` would print `NaN%`.
 * "Not observed" and "observed at 0%" are genuinely different answers here, so
 * the dash is the honest rendering of the first.
 */
export function formatPercentageOrDash(value: number | null | undefined, digits = 1): string {
  return value === null || value === undefined ? '—' : formatPercentage(value, digits)
}

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
 * Data Dragon lists one entry per playable champion *kit*, not per champion.
 * Patch 16.15 ("League classique") added 60 legacy kits reusing the original
 * champion's display name — `Jade_Ahri` with key `60103` next to Ahri's `103`.
 * The ingestor only aggregates queue 420, so those ids never carry a single
 * stat: left in, they duplicate every search hit and picker row with a dead
 * end, and put 60 empty pages in the sitemap.
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

/**
 * DDragon's champion key (`Ahri`, `MasterYi`, `Nunu`) as the URL slug (#1124).
 *
 * Lower-casing is the whole transform, on purpose. It is tempting to derive the
 * slug from the *display* name instead, which is what a reader sees — but that
 * is lossy and unstable: `Nunu & Willump` and `Bel'Veth` would need punctuation
 * rules, and Riot renames display names (`Cho'Gath`) without touching the key.
 * The key is the stable identifier, so the slug is a pure function of it and the
 * mapping can never drift between the sitemap, the links and the router.
 */
export function toChampionSlug(ddragonId: string): string {
  return ddragonId.toLowerCase()
}

export function getSummonerSpellImageUrl(imageFileName: string, patch?: string | null): string | null {
  const normalizedPatch = normalizeDataDragonPatch(patch)
  if (!normalizedPatch) {
    return null
  }

  return `https://ddragon.leagueoflegends.com/cdn/${normalizedPatch}/img/spell/${imageFileName}`
}

export function getChampionSpellImageUrl(imageFileName: string, patch?: string | null): string | null {
  const normalizedPatch = normalizeDataDragonPatch(patch)
  if (!normalizedPatch) {
    return null
  }

  return `https://ddragon.leagueoflegends.com/cdn/${normalizedPatch}/img/spell/${imageFileName}`
}

export function getPositionIconUrl(position: string): string {
  return `/positions/icon-position-${position.toLowerCase()}.png`
}

export function getProfileIconUrl(profileIconId: number, patch?: string | null): string | null {
  const normalizedPatch = normalizeDataDragonPatch(patch)
  if (!normalizedPatch) {
    return null
  }

  return `https://ddragon.leagueoflegends.com/cdn/${normalizedPatch}/img/profileicon/${profileIconId}.png`
}
