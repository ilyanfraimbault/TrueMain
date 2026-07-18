import type { RegionSlug } from '~~/shared/types/leaderboard'

// Maps a Riot platform id (e.g. `EUW1`, `KR`) to the region slug the UI
// exposes as a flag. Mirrors the backend's RegionFilterParser.RouteToSlug:
// Korea is its own slug even though Riot groups KR+JP1 under Asia, and the
// JP/SEA shards have no pill in V1 — those return null so the flag component
// falls back to its neutral globe glyph.
const PLATFORM_TO_REGION: Record<string, RegionSlug> = {
  EUW1: 'europe',
  EUN1: 'europe',
  RU: 'europe',
  TR1: 'europe',

  NA1: 'americas',
  BR1: 'americas',
  LA1: 'americas',
  LA2: 'americas',
  OC1: 'americas',

  KR: 'korea',
}

export function platformIdToRegion(platformId: string | null | undefined): RegionSlug | null {
  if (!platformId) {
    return null
  }
  return PLATFORM_TO_REGION[platformId.toUpperCase()] ?? null
}
