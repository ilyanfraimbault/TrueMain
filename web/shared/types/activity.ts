// Mirrors backend/Api/ReadModels/Truemains/TruemainActivityReadModel.cs.
// See #927, reshaped in #1473.

/**
 * Which window of the activity grid a series covers. The *unit* does not change
 * with it — every cell is a UTC day, except on `day`, the one window narrow
 * enough that there are no days left to draw and the cells become the games.
 */
export type ActivityMode = 'month' | 'patch' | 'week' | 'day'

export interface TruemainActivityResponse {
  /** The last thirty UTC days, clamped to the oldest game retention still holds. */
  month: ActivitySeries
  /** Every UTC day of the current patch. The default view. */
  patch: ActivitySeries
  /** The last seven UTC days, today included. */
  week: ActivitySeries
  /** Today's games, one cell each; empty on a rest day. */
  day: ActivitySeries
}

export interface ActivitySeries {
  mode: ActivityMode
  /**
   * The `major.minor` patch the window covers, on the patch series only. Also
   * `null` there when no tracked match carries a parseable patch yet.
   */
  patch: string | null
  /** ISO-8601 start of the window; `null` when the series holds no cell. */
  coverageFromUtc: string | null
  /** ISO-8601 start of the window's last cell; `null` when the series holds none. */
  coverageToUtc: string | null
  /** Cells, oldest first. */
  buckets: ActivityBucket[]
  games: number
  wins: number
  /** `null` — not `0` — when the series holds no games. */
  winRate: number | null
}

export interface ActivityBucket {
  /** `yyyy-MM-dd` on a calendar window, the match id on the day window. */
  key: string
  /** ISO-8601 start of the day, or of the game. Never null. */
  startUtc: string
  /** `0` is a real answer: a day the player did not queue. */
  games: number
  wins: number
  /**
   * `null` — not `0` — when `games` is 0. This is the wire-level difference
   * between "played and lost everything" and "did not play"; the two must never
   * render alike.
   */
  winRate: number | null
  /** Champion played, on the day window's per-game cells only. */
  championId: number | null
}
