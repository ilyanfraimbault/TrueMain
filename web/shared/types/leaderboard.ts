// The leaderboard's query vocabulary: its regions and its sort columns (#162).

/**
 * Every region slug the leaderboard exposes, as a runtime list.
 *
 * This is the single source of truth: {@link RegionSlug} is *derived* from it
 * rather than declared beside it, so the two cannot drift. That matters because
 * the runtime copies are validation guards — they narrow untrusted input (query
 * strings, `localStorage`) against this list, and a slug present in the type but
 * missing from the list is silently dropped to `null` instead of failing loudly.
 *
 * JP1 and SEA shards are out of scope until there's demand. Adding a region
 * touches more than this file — the display pills in `LeaderboardFilters.vue`
 * and `home/TruemainsPanel.vue`, the flag artwork in `LeaderboardRegionFlag.vue`,
 * and the platform mapping in `shared/utils/region.ts` all carry their own
 * per-region entries and must be extended too.
 */
export const REGION_SLUGS = ['europe', 'americas', 'korea'] as const

export type RegionSlug = typeof REGION_SLUGS[number]

/**
 * Ranking column of the leaderboard. `rank` is current ranked standing (the
 * default); `dedication` ranks by the truemain dedication score; `games`,
 * `kda` and `winRate` rank by the row's Games / KDA / WR figure (#1737). Every
 * order is descending. Anything the backend doesn't recognise falls back to
 * `rank`.
 */
export const LEADERBOARD_SORTS = ['rank', 'dedication', 'games', 'kda', 'winRate'] as const

export type LeaderboardSort = typeof LEADERBOARD_SORTS[number]
