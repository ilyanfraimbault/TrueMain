// The health cockpit — `GET /api/ops/pipeline-health` (#1031).

import type { DetectorStatus } from './common'
import type { ProcessRunStatus } from './processes'

/**
 * One cockpit signal (#1031): a verdict, the sentence behind it, and the route that
 * owns its detail. The cockpit holds no depth of its own — every tile is a link.
 */
export interface PipelineHealthSignal {
  /** `processes` | `dataQuality` | `ingestionLag` | `diskForecast`. */
  key: string
  title: string
  status: DetectorStatus
  /** The one sentence explaining this signal's state. */
  headline: string
  /**
   * Set if and only if `status` is `unknown`. Rendered in place of a number, because
   * a zero here would read as a pass.
   */
  unknownReason: string | null
  /** Admin route owning the detail: `/processes`, `/data-quality`, `/database`. */
  detailPath: string
}

/** Effective status of one pipeline process, PascalCase as `/ops/process-runs` spells it. */
export type ProcessHealthStatus = ProcessRunStatus | 'Missing'

/** One process's run health. Timestamps are null when it has never recorded a run. */
export interface ProcessHealth {
  processName: string
  status: ProcessHealthStatus
  lastStartedAtUtc: string | null
  lastFinishedAtUtc: string | null
  /** Null when the process has never succeeded — not the same as "succeeded long ago". */
  lastSuccessAtUtc: string | null
  /** Terminal runs since the last success; 0 when the latest run succeeded or was skipped. */
  consecutiveFailures: number
  durationMs: number
  error: string | null
}

/** Newest ingested match and patch on one platform. */
export interface PlatformRawDataFreshness {
  platformId: string
  latestMatchStartAtUtc: string | null
  latestPatchVersion: string
}

/** Raw-corpus counters, scoped to the configured ranked queue. */
export interface RawDataFreshness {
  queueId: number
  rawMatchCount: number
  rawParticipantCount: number
  platforms: PlatformRawDataFreshness[]
}

/** The two pipeline gaps. Null means "nothing to measure", never zero. */
export interface PipelineGaps {
  matchIngestionToMainAnalysisMinutes: number | null
  championDataLagMinutes: number | null
}

/**
 * `GET /api/ops/pipeline-health` — the health cockpit's single payload (#1031).
 * One rolled-up verdict over the signals below, each of which links to the panel that
 * owns its detail.
 */
export interface PipelineHealth {
  status: DetectorStatus
  /** The verdict as one actionable sentence. */
  headline: string
  /** Stated on the page: a cockpit that hides its age gets read as live. */
  evaluatedAtUtc: string
  /** Severity-ordered, worst first. */
  signals: PipelineHealthSignal[]
  processes: ProcessHealth[]
  rawData: RawDataFreshness
  gaps: PipelineGaps
  /** Absent from an API older than #1153. */
  regionBalance?: RegionBalance
}

/** One platform's row of the region-balance panel (#1153). */
export interface PlatformBalance {
  platformId: string
  /** Whether the match-ingest claim allocates to it; null when the claim's platforms are unknown. */
  inClaim: boolean | null
  /** Tracked Riot accounts, any status. */
  accounts: number
  /** Distinct accounts holding at least one active main. */
  activeMainAccounts: number
  /** Ranked matches ingested within the window (by ingestion time). */
  matchesInWindow: number
  /** Share of every platform's window total; null when nothing was ingested. */
  matchShare: number | null
  /** Mean per-champion coverage deficit in [0, 1] — the allocator's signal. Null when unknown. */
  meanCoverageDeficit: number | null
  /** Champions of the shared universe below the target here. Null when unknown. */
  championsBelowTarget: number | null
  championsBelowTargetShare: number | null
  /** Share of a claim batch the allocator gives it; null outside the claim or when unknown. */
  claimShare: number | null
}

/** Matches ingested on one platform on one UTC day. Days without a row ingested nothing. */
export interface PlatformDailyMatches {
  /** `YYYY-MM-DD`, UTC. */
  day: string
  platformId: string
  matches: number
}

/** Per-region balance (#1153). Informational: it does not move the verdict. */
export interface RegionBalance {
  /** When measured — cached up to 5 min, so it can trail `evaluatedAtUtc`. */
  measuredAtUtc: string
  windowDays: number
  windowStartUtc: string
  /** `Coverage:TargetMainsPerChampion` as the Ingestor published it; null when unknown. */
  targetMainsPerChampion: number | null
  claimPlatforms: string[] | null
  configurationCapturedAtUtc: string | null
  /** Set iff the target is unknown — the coverage columns are then null, never zero. */
  coverageUnknownReason: string | null
  /** Champions with an active main anywhere — the deficit's denominator. */
  championUniverse: number
  platforms: PlatformBalance[]
  dailyMatches: PlatformDailyMatches[]
  /** Set when the panel could not be measured at all. */
  unknownReason: string | null
}
