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
