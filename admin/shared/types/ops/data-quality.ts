// Data-quality checks and detectors — `GET /api/ops/data-quality/*`.

import type { BadgeColor, DetectorStatus } from './common'

/**
 * The data-quality checks, camelCase on the wire. Each check is independently
 * listable and queue-scoped (lane checks don't fire on ARAM):
 *   - `missingTimeline`      — TimelineIngested=false past the staleness window
 *   - `wrongParticipantCount`— row count ≠ the queue's expected count
 *   - `missingTeamPosition`  — a team missing one of the 5 lanes (SR only)
 *   - `zeroDuration`         — GameDurationSeconds = 0
 *   - `duplicateChampion`    — same champion twice on one team (SR only)
 */
export type DataQualityIssueType
  = | 'missingTimeline'
    | 'wrongParticipantCount'
    | 'missingTeamPosition'
    | 'zeroDuration'
    | 'duplicateChampion'

/**
 * Presentation metadata for one issue type — label, icon and badge color. Drives
 * the filter select, group headers and badges so the panel stays consistent.
 * Keyed by `DataQualityIssueType` in `ISSUE_META` on the data-quality page.
 */
export interface IssueMeta {
  label: string
  icon: string
  color: BadgeColor
  description: string
}

/** A single flagged match row in the list. */
export interface FlaggedMatch {
  matchId: string
  platformId: string
  queueId: number
  gameStartTimeUtc: string
  gameDurationSeconds: number
  timelineIngested: boolean
  participantCount: number
  /** Expected count for the queue, or null when the queue has no profile. */
  expectedParticipantCount: number | null
  /** Every check this match trips (a match can appear in several groups). */
  issues: DataQualityIssueType[]
}

/** One issue type's flagged matches: a capped sample plus the full count. */
export interface DataQualityIssueGroup {
  issueType: DataQualityIssueType
  count: number
  matches: FlaggedMatch[]
}

/** `GET /api/ops/data-quality/incomplete-matches` — flagged matches by issue. */
export interface IncompleteMatchesResponse {
  groups: DataQualityIssueGroup[]
  /** Distinct matches flagged by at least one active check. */
  total: number
  page: number
  pageSize: number
  /** Hours a missing timeline must age before it's flagged (vs normally pending). */
  staleTimelineThresholdHours: number
}

/** Filters for `GET /api/ops/data-quality/incomplete-matches`. */
export interface IncompleteMatchesFilters {
  /** Restrict to a single check; omit for all. */
  issue?: DataQualityIssueType
  /** Restrict to one queue id (e.g. 420); omit for all. */
  queue?: number
  /** Only consider matches at least this many hours old. */
  minAgeHours?: number
  /** 1-based page index for each issue group's sample. */
  page?: number
  /** Per-issue sample size; backend clamps to [1, 100], default 25. */
  pageSize?: number
}

/**
 * One position slot on a team. For lane queues `position` is one of the five
 * canonical lanes and `filled` is false for a gap; for laneless queues
 * `position` is empty and every slot is filled.
 */
export interface MatchSlot {
  /** Canonical lane name for lane queues; empty for laneless queues. */
  position: string
  /** False when this lane slot has no participant (a gap to highlight). */
  filled: boolean
  participantId: number | null
  championId: number | null
  summonerName: string | null
  win: boolean | null
  /** True when this slot shares its champion with another slot on the team. */
  duplicateChampion: boolean
}

/** One team's roster, laid out by position with gaps highlighted. */
export interface MatchTeam {
  teamId: number
  /** Actual participant rows ingested for this team. */
  playerCount: number
  /** Players a complete team should carry, or null when the queue is unknown. */
  expectedPlayerCount: number | null
  /**
   * Members whose position didn't map onto a canonical lane (unknown/duplicate
   * position). They exist — the team isn't short — so they're reported as
   * unplaced, never as missing players. Always 0 for laneless queues.
   */
  unplacedCount: number
  /** Team result, or null when the team has no ingested rows. */
  win: boolean | null
  slots: MatchSlot[]
}

/** `GET /api/ops/data-quality/match/{id}` — per-match detail. */
export interface MatchDataQualityDetail {
  matchId: string
  platformId: string
  queueId: number
  gameMode: string
  gameStartTimeUtc: string
  gameDurationSeconds: number
  gameVersion: string
  timelineIngested: boolean
  participantCount: number
  expectedParticipantCount: number | null
  /** True when the queue has a known profile (count/position rules apply). */
  queueKnown: boolean
  /** True when TeamPosition is meaningful for this queue. */
  hasLanes: boolean
  issues: DataQualityIssueType[]
  teams: MatchTeam[]
}


/** One drill-down row: an audited table, platform, process, check or patch. */
export interface DataQualityDetectorRow {
  label: string
  status: DetectorStatus
  /** The row's number, or null when it could not be measured. */
  value: number | null
  /** The number as it should be printed, with its unit; null when unmeasured. */
  valueLabel: string | null
  valueStatus: DetectorStatus | null // the printed number's own verdict; null when `status` judged it
  note: string | null
}

/** One configured green/amber/red boundary, echoed so the panel can state it. */
export interface DataQualityThreshold {
  label: string
  /** Null when the level is disabled (configured to 0 or less). */
  amber: number | null
  red: number | null
  unit: 'count' | 'percent' | 'hours' | 'ratio'
  /**
   * Which side of the level is the bad one. `below` marks a floor (patch volume
   * against the median); everything else is a ceiling. The number alone does not
   * say which, and printing a floor as a ceiling inverts its meaning.
   */
  direction: 'above' | 'below'
}

/** One detector's card (#924). */
export interface DataQualityDetector {
  key: string
  title: string
  status: DetectorStatus
  /** Headline number, or null when unknown. */
  count: number | null
  countLabel: string
  headline: string
  /** Set if and only if `status` is `unknown`. */
  unknownReason: string | null
  /** Which tables the detector reads and why that is affordable on a page view. */
  sourceNote: string
  rows: DataQualityDetectorRow[]
  thresholds: DataQualityThreshold[]
  /** True when a heavier on-demand endpoint can expand this detector. */
  hasDrillDownEndpoint: boolean
}

/** `GET /api/ops/data-quality/detectors` — the automated anomaly detectors. */
export interface DataQualityDetectorsResponse {
  detectors: DataQualityDetector[]
  /** Every age on the panel is relative to this, not to the browser's clock. */
  evaluatedAtUtc: string
}

/** One champion's aggregate freshness on a patch. */
export interface ChampionFreshnessRow {
  championId: number
  patch: string
  lastAggregatedAtUtc: string
  ageHours: number
  /** Scope rows behind the reading, so a one-account champion reads as thin. */
  scopeRows: number
  status: DetectorStatus
}

/** `GET /api/ops/data-quality/aggregate-freshness` — on-demand breakdown. */
export interface AggregateFreshnessResponse {
  patches: string[]
  champions: ChampionFreshnessRow[]
  championCount: number
  staleChampionCount: number
  staleAfterHours: number
  evaluatedAtUtc: string
}
