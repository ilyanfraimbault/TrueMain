// Database storage footprint and growth forecast — `GET /api/ops/db/*`.

export type StorageEngine = 'postgres' | 'mongo'

/**
 * One row of `GET /api/ops/db/tables` (sorted by `totalBytes` desc across both
 * engines — the question is "what is biggest on this disk", which does not stop
 * at an engine boundary).
 */
export interface DbTableRow {
  engine: StorageEngine
  /** Postgres table name, or Mongo collection name. */
  tableName: string
  /** Planner estimate for Postgres, exact document count for Mongo. */
  rowEstimate: number
  totalBytes: number
  tableBytes: number
  indexBytes: number
}

/**
 * `GET /api/ops/db/history` — storage growth over a window plus the disk
 * forecast (#925). Read from the daily snapshot collection, never from a live
 * `pg_catalog` scan, so the page stays cheap however far back it looks.
 *
 * Everything is empty until the ingestor's storage-snapshot step has run at
 * least once, and `forecast` stays null until there are three days to fit.
 */
export interface DbStorageHistory {
  daily: DbStorageDailyPoint[]
  /** The largest objects only — smaller ones still count in `daily` totals. */
  tables: DbStorageTableSeries[]
  /**
   * The engines the window actually holds readings for. Says what the totals
   * cover instead of implying they are the whole disk: before the first Mongo
   * snapshot lands, and wherever Mongo is unconfigured, this is postgres alone.
   */
  engines: StorageEngine[]
  /**
   * How many of the most recent days the forecast is allowed to fit: the trailing
   * run of days measuring the same engines as the latest one. Equal to
   * `daily.length` in the steady state, smaller only just after an engine started
   * or stopped being measured. The panel reads this rather than re-deriving the
   * rule, so its explanation cannot drift from the backend's behaviour.
   */
  comparableDays: number
  /** Null when no honest projection is possible; see `DbStorageForecast`. */
  forecast: DbStorageForecast | null
}

export interface DbStorageDailyPoint {
  dateUtc: string
  /** Postgres + Mongo on-disk size — what actually occupies the volume. */
  databaseBytes: number
  /** The Postgres half of `databaseBytes`; 0 if the day has no Postgres reading. */
  postgresBytes: number
  /** The Mongo half; 0 for every day before #1023 and where Mongo is unconfigured. */
  mongoBytes: number
  /** Sum of per-object sizes; smaller than `databaseBytes` (no catalogs). */
  totalBytes: number
  rowEstimate: number
}

export interface DbStorageTableSeries {
  engine: StorageEngine
  tableName: string
  points: DbStorageTablePoint[]
  currentBytes: number
  bytesPerDay: number
  rowsPerDay: number
  /** Growth over the window as a fraction (0.25 = +25%); null if it started empty. */
  growthRate: number | null
}

export interface DbStorageTablePoint {
  dateUtc: string
  totalBytes: number
  rowEstimate: number
}

/**
 * Null on the parent when fewer than 3 days of history exist, when storage is
 * flat or shrinking, or when no disk capacity is configured — the panel explains
 * which rather than showing a made-up date.
 *
 * "Days of history" counts only the days measuring the same engines as the most
 * recent one: the day Mongo first appears adds its whole footprint at once, and
 * fitting a trend across that step would read a one-off jump as a daily rate.
 */
export interface DbStorageForecast {
  bytesPerDay: number
  diskCapacityBytes: number
  crossings: DbStorageThresholdCrossing[]
}

export interface DbStorageThresholdCrossing {
  percent: number
  thresholdBytes: number
  /** Null = no meaningful date at this rate (over a century either way). A past date = already breached. */
  projectedAtUtc: string | null
}
