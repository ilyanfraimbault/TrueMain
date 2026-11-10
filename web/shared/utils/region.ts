import type { RegionSlug } from '../types/leaderboard'

/**
 * The regions the UI offers, one entry each: the label a pill or a flag reads,
 * and the Riot platforms the region groups. Mirrors the backend's
 * `RegionFilterParser.RouteToSlug`: Korea is its own region even though Riot
 * groups KR and JP1 under Asia, and the JP/SEA shards have no region yet, so
 * they map to none and the flag falls back to its neutral globe. The first
 * platform is the region's own (the one a new account there lands on).
 *
 * A new region is an entry here, its slug in `REGION_SLUGS` and its flag in the
 * layer's `region` icon collection (`i-region-{slug}`).
 */
export const REGIONS: Record<RegionSlug, { label: string, platforms: readonly string[] }> = {
  europe: { label: 'Europe', platforms: ['EUW1', 'EUN1', 'RU', 'TR1'] },
  americas: { label: 'Americas', platforms: ['NA1', 'BR1', 'LA1', 'LA2', 'OC1'] },
  korea: { label: 'Korea', platforms: ['KR'] },
}

const PLATFORM_TO_REGION: ReadonlyMap<string, RegionSlug> = new Map(
  (Object.entries(REGIONS) as [RegionSlug, typeof REGIONS[RegionSlug]][])
    .flatMap(([slug, region]) => region.platforms.map(platform => [platform, slug] as const)),
)

export function platformIdToRegion(platformId: string | null | undefined): RegionSlug | null {
  if (!platformId) return null
  return PLATFORM_TO_REGION.get(platformId.toUpperCase()) ?? null
}

/** The region a slug from the API names (`""` for a shard with none), or null. */
export function regionOf(slug: string | null | undefined): RegionSlug | null {
  return slug && Object.hasOwn(REGIONS, slug) ? slug as RegionSlug : null
}

/** The region's flag icon, from the layer's `region` collection. */
export function regionIcon(region: RegionSlug): string {
  return `i-region-${region}`
}
