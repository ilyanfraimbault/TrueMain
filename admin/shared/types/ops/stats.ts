// Headline counters, champion stats and match volume series — `GET /api/ops/stats/*`.

/** X-axis granularity for `GET /api/ops/stats/matches-over-time`. */
export type MatchTimeGranularity = 'day' | 'week' | 'month' | 'year' | 'patch'

/**
 * Granularities of the ingestion-throughput series. Narrower than
 * `MatchTimeGranularity` on purpose: a patch is a property of the games, not of
 * when we ingested them, and a year cannot fill two buckets under the 180-day
 * run retention. `hour` exists for the candidate-stock series (#1403), whose two
 * transient statuses are invisible at daily resolution; the flow panels accept it
 * but do not offer it.
 */
export type IngestionTimeGranularity = 'hour' | 'day' | 'week' | 'month'
