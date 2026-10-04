// Patch coverage — `GET /api/ops/patch-coverage` (#1033).

import type { DetectorStatus } from './common'

/**
 * Why a patch is or is not servable. `notAggregated` and `thin` are deliberately
 * distinct: both mean "almost nothing clears the floor", and they call for
 * opposite reactions — wait for the fold, versus stop trusting the patch.
 */
export type PatchVerdict = 'servable' | 'thin' | 'notAggregated' | 'unknown'

/** One game date's ingestion on a patch. */
export interface PatchCoverageDay {
  /** UTC game date, ISO `yyyy-MM-dd`. */
  date: string
  matches: number
  participants: number
}

/** A `(champion, lane)` line that has games but not enough of them. */
export interface PatchThinLine {
  championId: number
  position: string
  games: number
  /** Games still missing before the line clears the floor. */
  gamesToFloor: number
}

/** One aggregation fold's state on one patch. */
export interface PatchFoldCoverage {
  key: string
  label: string
  /**
   * False when the patch predates the fold entirely (#920 bans, #957 per-opponent
   * spikes). Every count is then `null` rather than `0`: raw matches are not kept,
   * so those rows are absent by construction, not missing — and a zero would read
   * as "the fold is broken on this patch".
   */
  measured: boolean
  /** Oldest patch the fold has any row on, or null when it has none at all. */
  firstMeasuredPatch: string | null
  /** Set if and only if `measured` is false. */
  notMeasuredNote: string | null
  rows: number | null
  champions: number | null
  lastAggregatedAtUtc: string | null
  ageHours: number | null
  status: DetectorStatus
  /** Matches still to fold, or null for folds carrying no per-match flag. */
  pendingMatches: number | null
  note: string | null
}

/** One patch's ingestion, aggregate coverage and per-fold state. */
export interface PatchCoverageRow {
  patch: string
  /** True for the patch the public reads currently resolve to. */
  isCurrent: boolean
  verdict: PatchVerdict
  status: DetectorStatus
  headline: string
  matches: number
  participants: number
  firstGameStartUtc: string | null
  lastGameStartUtc: string | null
  daily: PatchCoverageDay[]
  /** `(champion, lane)` pairs holding at least one aggregate row. */
  lines: number
  linesPastFloor: number
  champions: number
  championsPastFloor: number
  /** The bar `linesPastFloor` was judged against; null when the patch was not judged. */
  servableLinesBar: number | null
  servableLinesBarNote: string | null
  belowFloorCount: number
  belowFloor: PatchThinLine[]
  folds: PatchFoldCoverage[]
}

/** `GET /api/ops/patch-coverage` — is the current patch servable? (#1033) */
export interface PatchCoverageResponse {
  queueId: number
  /** Echoed from `ChampionsList:MinSampleGames`, never re-declared admin-side. */
  minSampleGames: number
  floorNote: string
  /** Newest patch holding an aggregate row — what the public reads resolve to. */
  currentPatch: string | null
  verdict: PatchVerdict
  status: DetectorStatus
  headline: string
  /**
   * Why no verdict could be given. Set only when a measurement failed — without the
   * coverage rollup, `thin` and `notAggregated` are indistinguishable, and guessing
   * between them is worse than saying nothing.
   */
  unknownReason: string | null
  patches: PatchCoverageRow[]
  sourceNote: string
  evaluatedAtUtc: string
}
