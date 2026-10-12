// Headline counters, champion stats and match volume series — `GET /api/ops/stats/*`.

/** Candidate pipeline buckets — `GET /api/ops/stats/overview` → `candidatesByStatus`. */
export interface CandidatesByStatus {
  New: number
  Scored: number
  Queued: number
  Processing: number
  Validated: number
}

/** `GET /api/ops/stats/overview`. */
export interface OverviewStats {
  trackedAccounts: number
  totalMatches: number
  totalParticipants: number
  candidatesByStatus: CandidatesByStatus
  totalMains: number
  totalOtps: number
  distinctChampionsWithGames: number
  distinctChampionsWithMains: number
  matchesLast7Days: number
  matchesLast30Days: number
}

/**
 * One row of `GET /api/ops/stats/champions` (sorted by `games` desc).
 *
 * NOTE: `mains`, `otps` and `extendedSamples` honor the `region` filter only —
 * they ignore `patch`/`position`/`queue`. `games` honors every filter.
 */
export interface ChampionStatsRow {
  championId: number
  games: number
  mains: number
  otps: number
  extendedSamples: number
}

/** Filters for `GET /api/ops/stats/champions`. Empty/undefined = no filter. */
export interface ChampionStatsFilters {
  /** PlatformId, e.g. `EUW1` / `KR` / `NA1`. */
  region?: string
  /** Normalized MAJOR.MINOR patch, e.g. `16.4`. */
  patch?: string
  /** `TOP` | `JUNGLE` | `MIDDLE` | `BOTTOM` | `UTILITY`. */
  position?: string
  /** Queue id, e.g. `420`. */
  queue?: number
}

/** X-axis granularity for `GET /api/ops/stats/matches-over-time`. */
export type MatchTimeGranularity = 'day' | 'week' | 'month' | 'year' | 'patch'

/**
 * One bucket of `GET /api/ops/stats/matches-over-time` (returned in chronological
 * order). Matches are counted by GAME date (`Match.GameStartTimeUtc`).
 *
 * `bucket` shape depends on the requested granularity:
 *   - day/week/month/year: ISO-8601 UTC timestamp of the period start
 *     (e.g. `2026-06-01T00:00:00Z`) — format the label client-side per granularity.
 *   - patch: the normalized `MAJOR.MINOR` version string (e.g. `16.4`) — use as-is.
 */
export interface MatchTimeBucket {
  bucket: string
  matches: number
}

/**
 * Granularities of the ingestion-throughput series. Narrower than
 * `MatchTimeGranularity` on purpose: a patch is a property of the games, not of
 * when we ingested them, and a year cannot fill two buckets under the 180-day
 * run retention. `hour` exists for the candidate-stock series (#1403), whose two
 * transient statuses are invisible at daily resolution; the flow panels accept it
 * but do not offer it.
 */
export type IngestionTimeGranularity = 'hour' | 'day' | 'week' | 'month'

/**
 * `GET /api/ops/stats/matches-ingested` — how many matches the pipeline actually
 * ingested per period (#1025), from the recorded MatchIngestion run summaries.
 *
 * A different question from `matches-over-time`, which buckets games by when they
 * were *played*: that one barely moves when ingestion stalls, and grows in the
 * past when a backfill lands. Sourced from run summaries rather than
 * `matches.CreatedAtUtc` because retention deletes matches, which would make an
 * old bucket shrink over time — a curve rewriting its own history.
 */
export interface MatchesIngested {
  /** Oldest first. Quiet periods inside the observed range are present at zero. */
  buckets: MatchesIngestedBucket[]
  /** The effective window in days, after the backend clamped the request. */
  windowDays: number
  /** The process_runs TTL in days — how far back run history can possibly go. */
  retentionDays: number
  /** Start of the oldest run seen, or null when the window holds none. */
  earliestRunAtUtc: string | null
}

export interface MatchesIngestedBucket {
  /** ISO-8601 UTC period start, same shape as `MatchTimeBucket.bucket`. */
  bucket: string
  matchesInserted: number
  /**
   * Seen and not written. Since #1358 that means "already ingested" only — the
   * off-queue discards became their own counter on the run summary. Carried because
   * inserted-alone cannot tell "nothing to do" from "working hard and storing
   * nothing", which are opposite operational states.
   */
  matchesSkipped: number
  timelinesUpdated: number
  /** Ingestion runs started in the period, summary or not. */
  runs: number
}

/** Which engine a storage object belongs to (#1023). Both share one volume. */
