// Main candidates — `GET /api/ops/candidates` (the ingestion pipeline list).

import type { SeedRequestReadModel } from './seeds'

/**
 * Lifecycle of a main candidate (the ingestion pipeline):
 *   New        — surfaced from mastery, not yet scored
 *   Scored     — a main-likelihood score has been computed
 *   Queued     — selected for full ingestion
 *   Processing — the Ingestor is pulling the account's matches
 *   Validated  — confirmed as a main and fully ingested
 *   Rejected   — ruled out (not a main)
 */
export type MainCandidateStatus
  = | 'New'
    | 'Scored'
    | 'Queued'
    | 'Processing'
    | 'Validated'
    | 'Rejected'

/**
 * One row of `GET /api/ops/candidates`. `gameName`/`tagLine` are joined from the
 * `RiotAccount` on PUUID and are `null` until the account has been resolved (a
 * candidate is discovered from mastery before its account is upserted).
 */
export interface CandidateRow {
  id: string
  platformId: string
  puuid: string
  gameName: string | null
  tagLine: string | null
  championId: number
  championPoints: number
  championRankInMasteryTop: number
  score: number
  status: MainCandidateStatus
  discoveredAtUtc: string
  scoredAtUtc: string | null
  validatedAtUtc: string | null
  lastPlayTimeUtc: string
}

/** `GET /api/ops/candidates` — server-paginated candidate rows, most-relevant first. */
export interface CandidatesResponse {
  candidates: CandidateRow[]
  /** Total rows matching the filters (across all pages). */
  total: number
  page: number
  pageSize: number
}

/** Filters for `GET /api/ops/candidates`. Empty/undefined = no filter. */
export interface CandidatesFilters {
  /** A `MainCandidateStatus` name. */
  status?: MainCandidateStatus
  /** PlatformId, e.g. `EUW1` / `KR` / `NA1`. */
  region?: string
  /** Riot ID (gameName/tagLine), PUUID, or champion-id search. */
  search?: string
  /** 1-based page index. */
  page?: number
  /** Rows per page; backend clamps to [1, 100], default 25. */
  pageSize?: number
}

/**
 * `GET /api/ops/candidates/{id}` — one candidate's full detail: its pipeline
 * fields plus the ingested match count for its PUUID and the linked manual
 * `seedRequest` (matched on `resolvedPuuid` + platform), `null` when the
 * candidate was discovered organically by the ladder.
 */
export interface CandidateDetail extends CandidateRow {
  ingestedMatchCount: number
  seedRequest: SeedRequestReadModel | null
}

/**
 * `GET /api/ops/candidates/funnel` (#1024) — candidate throughput per period, read
 * from the recorded process-run summaries rather than from `main_candidates` row
 * counts: retention prunes stale candidates, so counting rows by status per past
 * period under-reports every bucket and increasingly so the further back it looks.
 * The whole series is therefore bounded by the `process_runs` TTL.
 */
export interface CandidateFunnel {
  buckets: CandidateFunnelBucket[]
  /** The requested window in days, after backend clamping. */
  windowDays: number
  /** How long run history is kept — the hard bound on `windowDays`. */
  retentionDays: number
  /**
   * Start of the oldest run in the window: the earliest period the series can speak
   * for. `null` when no run survives at all — an empty range, not a range of zeros.
   */
  earliestRunAtUtc: string | null
  /**
   * Start of the first run that recorded the validated counter, which the ingestor
   * only began writing with #1024. Buckets before it carry `validated: null`.
   */
  validatedFirstMeasuredAtUtc: string | null
}

/**
 * One period of the funnel. Intake is split by producing process because the three
 * sources fail independently — the ladder drying up and the harvest drying up are
 * different incidents with the same total.
 */
export interface CandidateFunnelBucket {
  /** Period start, ISO-8601 UTC. */
  bucket: string
  /** Candidates inserted by ladder discovery. */
  intakeLadder: number
  /** Candidates inserted by the orphan-participant harvest. */
  intakeHarvest: number
  /** Candidates an operator's manual seed pushed into the queue. */
  intakeManual: number
  scored: number
  /** Candidates promoted to the ingestion queue — the per-platform top-N. */
  promoted: number
  /** Accounts that cleared ingestion. `null`, not `0`, before the counter existed. */
  validated: number | null
  /** Accounts demoted back out of Validated on a critical play rate. */
  demoted: number
  /** Runs of any contributing process in this period; `0` means the pipeline was idle. */
  runs: number
}

/**
 * `GET /api/ops/candidates/stock` (#1403) — the candidate funnel's *level* per
 * period: how many rows sat in each status at the end of each period, from the hourly
 * snapshots the ingestor records.
 *
 * The companion of `CandidateFunnel`, which measures the same funnel's *flow*. Neither
 * derives from the other: a period that scored 5,000 candidates and promoted 5,000 out
 * of the pool leaves the level flat, and a flat level is also what a stalled pipeline
 * produces.
 *
 * Forward-only and never backfilled — periods before `earliestSnapshotAtUtc` are absent
 * from `buckets`, not zero. The level then was unmeasured, and unlike a counter it
 * cannot be reconstructed afterwards: `main_candidates` has no `QueuedAtUtc`, so Scored
 * and Queued are indistinguishable in the past, and pruning deletes rows outright.
 */
export interface CandidateStock {
  buckets: CandidateStockBucket[]
  /** The requested window in days, after backend clamping. */
  windowDays: number
  /** How long snapshot history is kept — the hard bound on `windowDays`. */
  retentionDays: number
  /** The oldest snapshot in the window; `null` when the step has never run. */
  earliestSnapshotAtUtc: string | null
  /** The most recent reading's timestamp, for the panel's "as of" line. */
  latestSnapshotAtUtc: string | null
}

/**
 * One period's level, summed across platforms.
 *
 * Sampled, never summed across time: a period holding several hourly readings reports
 * its *last* one, because adding two readings of the same 419,000 queued candidates
 * would report 838,000 of them. Across platforms within that one reading the counts
 * *are* summed — disjoint populations at a single instant.
 *
 * Every status is present on every bucket, zeros included: a recorded zero is a
 * measurement (`new: 0` means scoring drained its backlog, the healthy state) and must
 * stay distinguishable from an unmeasured period, which is absent from `buckets`.
 */
export interface CandidateStockBucket {
  /** Period start, ISO-8601 UTC. */
  bucket: string
  new: number
  scored: number
  queued: number
  processing: number
  validated: number
  /** Structurally 0 — no process assigns `Rejected` (#1029). Carried, not charted. */
  rejected: number
  /** The exact instant this period's reading was taken, ISO-8601 UTC. */
  sampledAtUtc: string
}

/**
 * `GET /api/ops/candidates/queue-latency` (#1024) — how long the candidates that
 * exist *right now* took to move through the queue. A snapshot over retained rows,
 * never a historical average: pruned candidates are not in it, and the surviving
 * population skews towards the ones that did move. Label it as such wherever shown.
 */
export interface CandidateQueueLatency {
  /** Discovery → scoring, over candidates that have been scored. */
  discoveredToScored: CandidateLatencyLeg
  /** Scoring → cleared ingestion, over candidates that have been validated. */
  scoredToValidated: CandidateLatencyLeg
  /** Candidate rows currently retained — the population every leg is drawn from. */
  retainedCandidates: number
  asOfUtc: string
}

/**
 * One leg of the queue. Both percentiles are `null` when `samples` is 0 — no row
 * carried both ends of the leg, which is not a latency of zero.
 */
export interface CandidateLatencyLeg {
  samples: number
  medianSeconds: number | null
  /** The slow tail: it diverging from the median is the shape of a backed-up queue. */
  p90Seconds: number | null
}
