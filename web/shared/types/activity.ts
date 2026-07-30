// Mirrors backend/Api/ReadModels/Truemains/TruemainActivityReadModel.cs.
// See #927.

/** Granularity of an activity series. */
export type ActivityMode = 'game' | 'day' | 'week' | 'patch'

/**
 * Which table the series was read from. `matches` is live `match_participants`,
 * hard-deleted by retention past ~2 patches; `aggregates` is the frozen
 * per-champion aggregate, which keeps every patch forever (#466).
 */
export type ActivitySource = 'matches' | 'aggregates'

/** Which games a series counts. */
export type ActivityScope = 'allChampions' | 'champion'

export interface TruemainActivityResponse {
  game: ActivitySeries
  day: ActivitySeries
  week: ActivitySeries
  patch: ActivitySeries
}

export interface ActivitySeries {
  mode: ActivityMode
  source: ActivitySource
  scope: ActivityScope
  /**
   * The champion a `champion`-scoped series is about. Also `null` on a
   * champion-scoped series when the player has no classified main — the series
   * is then empty rather than silently widened.
   */
  championId: number | null
  /**
   * True when the series can only see the retained match window, so a period it
   * does not cover may have been deleted rather than not played.
   */
  retentionBounded: boolean
  /** ISO-8601 start of the period the series speaks for; `null` when empty. */
  coverageFromUtc: string | null
  /** ISO-8601 start of the last period the series speaks for; `null` when empty. */
  coverageToUtc: string | null
  /** Cells, oldest first. */
  buckets: ActivityBucket[]
  games: number
  wins: number
  /** `null` — not `0` — when the series holds no games. */
  winRate: number | null
}

export interface ActivityBucket {
  /** Match id, `yyyy-MM-dd`, or `major.minor` depending on the mode. */
  key: string
  /** ISO-8601 start of the period; `null` on the patch series. */
  startUtc: string | null
  /** `0` is a real answer: a day the player did not queue. */
  games: number
  wins: number
  /**
   * `null` — not `0` — when `games` is 0. This is the wire-level difference
   * between "played and lost everything" and "did not play"; the two must never
   * render alike.
   */
  winRate: number | null
  /** Champion played, on the per-game series only. */
  championId: number | null
}
