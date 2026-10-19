// The champion directory and one champion's page header: summary, tier list, overview,
// the champion response and its trend / scaling series.
import type { ChampionBuild } from './builds'

export interface ChampionSummaryResponse {
  championId: number
  games: number
  wins: number
  winRate: number
  pickRate: number
  lanePlayRate: number
  trueMainCount: number
  /**
   * Share of observed matches that banned this champion, or null on a patch older
   * than ban ingestion (#920). Null means "not observed", not "never banned" —
   * render the gap, never a 0%. Not addable to `pickRate`: the two have different
   * denominators (every observed match vs tracked mains' games at this lane).
   */
  banRate: number | null
  /** OPGG-style performance tier: 'S' | 'A' | 'B' | 'C' | 'D' (patch-relative). */
  tier: string
  /**
   * The pickRate + banRate + winRate blended score (#971) that placed this row in
   * `tier`, scoped to this row's own position — both `/champions` and
   * `/champions/tierlist` tier one lane at a time, so this matches the score
   * `ChampionTierEntry` carries for the same (champion, position) row given the
   * same (patch, eloBracket).
   */
  tierScore: number
  position: string
  patchVersion: string
  lastUpdatedAtUtc: string
  topBuild: ChampionSummaryTopBuild | null
}

export interface ChampionSummaryTopBuild {
  firstItemId: number
  primaryKeystoneId: number
  secondaryStyleId: number
  itemPath: number[]
}

/**
 * Champion meta / tier-list for a single patch (`GET /champions/tierlist`).
 * Champions are bucketed into S/A/B/C/D tiers by a pickRate + banRate +
 * winRate blend (#971 — presence-first: pick and ban rate outweigh a
 * sample-shrunk win rate), tiered independently per position. All metrics
 * come from the same aggregates the directory reads — none are synthesised.
 */
export interface ChampionTierListResponse {
  patchVersion: string
  /** Position the list is scoped to, or null for every position at once. */
  position: string | null
  /** Tier groups in descending strength (S first); empty tiers are omitted. */
  tiers: ChampionTierGroup[]
}

export interface ChampionTierGroup {
  /** Tier letter: 'S' | 'A' | 'B' | 'C' | 'D'. */
  tier: string
  /** Rows in this tier, strongest-first. */
  entries: ChampionTierEntry[]
}

export interface ChampionTierEntry {
  championId: number
  position: string
  games: number
  winRate: number
  /** Share of TrueMain games on this position taken by this champion. */
  pickRate: number
  /** Share of observed matches that banned this champion; null before #920's data. */
  banRate: number | null
}

/**
 * Homepage-sized snapshot of the champion directory (`GET /champions/overview`,
 * #972): the lifetime "games analyzed" total (every aggregated game on every
 * patch, not just the rows the ranked directory keeps) plus a short, pre-sorted
 * slice of the strongest rows — so the homepage never fetches and sorts the full
 * ~500-row directory just to render a stat chip and an 8-row teaser. The teaser is
 * the active patch, unfiltered — the homepage has no patch or elo picker of its own.
 */
export interface ChampionOverviewResponse {
  /**
   * The patch `topRows` is ranked on — the patch the site serves, which is not
   * necessarily the newest one with data: a patch too thin to fill a directory is
   * skipped (#1109).
   */
  patchVersion: string
  /**
   * Every aggregated game the site holds, all patches summed — the lifetime total,
   * not this patch's. Says nothing about `patchVersion`, which scopes `topRows` only.
   */
  gamesAnalyzed: number
  /** Strongest rows, tier-then-games ordered (S first, busiest within a tier first), truncated to the requested limit. */
  topRows: ChampionOverviewRow[]
}

export interface ChampionOverviewRow {
  championId: number
  position: string
  /** OPGG-style performance tier: 'S' | 'A' | 'B' | 'C' | 'D' (patch-relative). */
  tier: string
  games: number
  winRate: number
  pickRate: number
  /** Share of observed matches that banned this champion; null before #920's data. */
  banRate: number | null
}

export interface ChampionResponse {
  championId: number
  patch: string
  position: string
  /**
   * Elo filter this slice was computed for: `ALL`, a bare tier (e.g. `GOLD` —
   * that tier only) or a `<TIER>_PLUS` form (e.g. `GOLD_PLUS` — that tier and
   * above). `ALL` by default.
   */
  eloBracket: string
  /**
   * Games in the selected bracket as a fraction of all games on this champion at
   * the resolved patch + position. `1` for the `ALL` bracket; lower for narrow
   * high-elo bands, so the page can flag how representative the slice is.
   */
  eloCoverage: number
  /**
   * False when `totalGames` is below the trustworthy-build floor (tiny
   * high-bracket slices). The page still renders the data but flags it as
   * low-confidence.
   */
  minSampleMet: boolean
  totalGames: number
  totalWins: number
  builds: ChampionBuild[]
}

export interface ChampionTrendResponse {
  championId: number
  position: string
  points: ChampionTrendPoint[]
}

export interface ChampionTrendPoint {
  patch: string
  winRate: number
  pickRate: number
  /**
   * Share of the patch's observed matches that banned this champion, all elo bands
   * (the trend endpoint takes no rank filter). Null on any patch older than ban
   * ingestion (#920) — early on, most of the series is null.
   */
  banRate: number | null
  games: number
}

/**
 * How a champion's win rate changes with game length, at a position. Win rate is
 * bucketed by game duration; `scalingIndex` is the win-rate gap between the
 * longest and shortest qualifying bucket (positive = scales into the late game).
 */
export interface ChampionScalingResponse {
  championId: number
  position: string
  patch: string | null
  buckets: ChampionScalingBucket[]
  scalingIndex: number | null
}

export interface ChampionScalingBucket {
  /** Duration bucket index, 0 (shortest) to 4 (longest). */
  bucket: number
  label: string
  games: number
  winRate: number
}
