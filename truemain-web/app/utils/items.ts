export function formatPercentage(value: number, digits = 1): string {
  return `${(value * 100).toFixed(digits)}%`
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

export function getItemImageUrl(itemId: number, patch?: string | null): string | null {
  const normalizedPatch = normalizeDataDragonPatch(patch)
  if (!normalizedPatch) {
    return null
  }

  return `https://ddragon.leagueoflegends.com/cdn/${normalizedPatch}/img/item/${itemId}.png`
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
  return `https://raw.communitydragon.org/latest/plugins/rcp-fe-lol-parties/global/default/icon-position-${position.toLowerCase()}.png`
}
