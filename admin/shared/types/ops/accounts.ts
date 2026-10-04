// Account explorer — `GET /api/ops/accounts/{nameTag}` (#1032).

import type { MainCandidateStatus } from './candidates'
import type { SeedRequestReadModel } from './seeds'

/**
 * The one-word verdict on a Riot ID, resolved server-side first-match-wins:
 *   NeverDiscovered   — no account row and no seed request. Says nothing about
 *                       whether the Riot ID exists: this read never calls Riot.
 *   SeedRequestedOnly — no account row, but an operator asked for it; the seed
 *                       request's own status/error says why it has not landed.
 *   Invalidated       — the PUUID 404s and AccountRefresh could not recover it.
 *                       Excluded from every selection: nothing will move again.
 *   Tracked           — in the match-ingestion population (queued candidate,
 *                       active main, or both).
 *   Retired           — had mains, MainActivity deactivated all of them (#900).
 *                       Rows are flagged, never deleted.
 *   NotAMain          — analysed, but nothing cleared the adaptive IsMain floor.
 *   CandidateOnly     — in the candidate funnel, never analysed.
 *   Discovered        — the account exists and nothing else has happened to it.
 */
export type AccountPipelineState
  = | 'NeverDiscovered'
    | 'SeedRequestedOnly'
    | 'Invalidated'
    | 'Tracked'
    | 'Retired'
    | 'NotAMain'
    | 'CandidateOnly'
    | 'Discovered'

/**
 * `GET /api/ops/accounts/{nameTag}?region=` — everything the pipeline knows about
 * one Riot ID. Never 404s: an unknown Riot ID is a populated response in the
 * `NeverDiscovered` state, because that is an answer this page exists to give.
 * 400 only on a malformed Riot ID or an unknown region.
 *
 * `identity`, `tracking` and `matchesIngested` are `null` together — they all
 * require a resolved account row.
 */
export interface AccountExplorer {
  query: AccountExplorerQuery
  state: AccountPipelineState
  /** The state in a sentence, built server-side. Render it verbatim. */
  stateDetail: string
  identity: AccountExplorerIdentity | null
  /**
   * Other accounts carrying the same Riot ID. `(gameName, tagLine, platformId)`
   * is deliberately not unique — Riot IDs are recyclable and collide across
   * regions — so the resolver picks the most recently active and lists the rest
   * here instead of arbitrating in silence. Usually empty.
   */
  otherAccountsWithSameRiotId: AccountExplorerAccountRef[]
  tracking: AccountExplorerTracking | null
  matchesIngested: AccountExplorerMatchesIngested | null
  /**
   * `main_candidates` rows, highest score first. Always empty when `identity` is
   * null: candidates are keyed on (platformId, puuid) and carry no Riot ID, so a
   * candidate whose account is not upserted yet cannot be found from a Riot ID.
   */
  candidates: AccountExplorerCandidate[]
  /** The manual "add a main" trail — the only reliable manual-seed signal. */
  seedRequest: SeedRequestReadModel | null
  mains: AccountExplorerMains
  /**
   * Most recent first, capped at 50. At most one row per UTC day, solo queue
   * only, never pruned — the one series here whose gaps are gaps in play.
   */
  rankSnapshots: AccountExplorerRankSnapshot[]
}

/** The request as the backend resolved it. */
export interface AccountExplorerQuery {
  gameName: string
  tagLine: string
  /** The requested platform id, or null when the search was region-wide. */
  region: string | null
}

/** The resolved account and the per-process freshness stamps. */
export interface AccountExplorerIdentity {
  riotAccountId: string
  puuid: string
  gameName: string
  tagLine: string | null
  platformId: string
  profileIconId: number
  summonerLevel: number
  /** `RiotAccountStatus` name: 'Active' or 'Invalid'. */
  status: string
  createdAtUtc: string
  updatedAtUtc: string
  /** Last successful account-v1 identity resolution. */
  lastProfileSyncAtUtc: string | null
  /** Last successful league-v4 read — stamped even when the rank was unchanged. */
  lastRankSyncAtUtc: string | null
  /** Can be newer than every main row's `calculatedAtUtc` — see `analysisSkipped`. */
  lastMainCalcAtUtc: string | null
  /** Last *successful* mastery check; a failed lookup leaves it untouched. */
  lastActivityCheckAtUtc: string | null
  lastMatchIngestAtUtc: string | null
  /** Rank sort key from the latest snapshot; `null` = never seen ranked, not 0. */
  rankScore: number | null
}

/** One of the other accounts sharing this Riot ID. */
export interface AccountExplorerAccountRef {
  riotAccountId: string
  puuid: string
  platformId: string
  status: string
  lastMatchIngestAtUtc: string | null
}

/**
 * Ingest-population membership and lease state. Every threshold that would turn
 * these into a verdict (claim lease, inactivity window, retained patch count) is
 * Ingestor config the API cannot see, so this section reports ages and stops —
 * it never claims a lease is stale. Judge `claimAgeSeconds` against
 * `MatchIngestion:ClaimLeaseMinutes` (30 by default) yourself.
 */
export interface AccountExplorerTracking {
  /** Derived, not a column: the two membership arms of the ingest claim. */
  isTracked: boolean
  trackedVia: 'EstablishedMain' | 'QueuedCandidate' | 'Both' | null
  hasActiveMain: boolean
  hasQueuedCandidate: boolean
  /** `MatchIngestStatus` name: 'Idle' or 'Processing'. */
  matchIngestStatus: string
  matchIngestClaimedAtUtc: string | null
  claimAgeSeconds: number | null
  lastMatchIngestAtUtc: string | null
  /** Claimable but its lease has never come up — the queue has not reached it. */
  neverIngested: boolean
}

/**
 * The three game counts that exist, each with the population it counts. They are
 * not three views of one number and must never be rendered as one: label each.
 */
export interface AccountExplorerMatchesIngested {
  /** Live participant rows: every champion, but bounded by retention. */
  liveParticipantCount: number
  /** Measured off the surviving rows, not derived from the retention config. */
  oldestRetainedGameStartUtc: string | null
  newestRetainedGameStartUtc: string | null
  /** Frozen aggregates: survive forever, but cover **main champions only**. */
  careerGamesFromAggregates: number
  aggregatedPatchCount: number
  /** A lower bound: a scope records its most recent game, not its first. */
  oldestAggregatedGameStartUtc: string | null
  /** Last MainAnalysis pass's sample size, capped at 50. A ceiling, not a total. */
  lastAnalysisSampleSize: number | null
  /**
   * True when the frozen aggregates prove games existed that the live rows no
   * longer hold. **False does not mean nothing was pruned** — the aggregates only
   * cover main champions. Render `prunedNote` either way; never show a bare 0.
   */
  pruned: boolean
  prunedNote: string
}

/** One `main_candidates` row and what the scorer had to work with. */
export interface AccountExplorerCandidate {
  id: string
  championId: number
  status: MainCandidateStatus
  /**
   * `MainCandidateSource` name. `ManualSeed` is never assigned in production —
   * ManualSeedProcess reuses the ladder upsert — so a manually seeded candidate
   * reads `Ladder`. Read `seedRequest` for the manual trail.
   */
  source: 'Ladder' | 'ManualSeed' | 'Harvest'
  score: number
  /**
   * The persisted inputs. The score's **components are not stored** — only the
   * final blend — so they cannot be shown, and recomputing them would mix today's
   * scarcity snapshot into a number produced against an older one.
   */
  scoreInputs: AccountExplorerCandidateScoreInputs
  discoveredAtUtc: string
  scoredAtUtc: string | null
  validatedAtUtc: string | null
}

/** Ladder candidates carry mastery rank/points; harvest ones carry observed games. */
export interface AccountExplorerCandidateScoreInputs {
  lastPlayTimeUtc: string
  championRankInMasteryTop: number
  championPoints: number
  observedGames: number
  /** Persisted but not a scoring input yet. */
  observedWins: number
}

export interface AccountExplorerMains {
  rows: AccountExplorerMainRow[]
  thresholds: AccountExplorerMainThresholds
}

/** The configured MainAnalysis thresholds a row's verdict should be read against. */
export interface AccountExplorerMainThresholds {
  /** Base play rate required for a well-covered champion (0.20). */
  playRateThreshold: number
  /** Lowest the adaptive threshold can drop to (0.12, #407). */
  playRateFloor: number
  otpPlayRateThreshold: number
  /** Below this, MainAnalysis refuses to overwrite an established main (#825). */
  minMatchesToEvaluate: number
  /** Why only a band is given. Render it next to the numbers. */
  effectiveThresholdNote: string
}

/** One `main_champion_stats` row. */
export interface AccountExplorerMainRow {
  championId: number
  /** The pass's sample size, not the account's total. */
  totalMatches: number
  championMatches: number
  playRate: number
  isMain: boolean
  isOtp: boolean
  /** A main only thanks to the coverage-relaxed floor (#407). */
  isExtendedSample: boolean
  isActive: boolean
  primaryPosition: string
  positionBreakdown: AccountExplorerPositionStat[]
  calculatedAtUtc: string
  /**
   * The last MainAnalysis run is newer than this row: the process looked and
   * declined to overwrite (thin-sample guard, #825). Not a stale-data bug.
   */
  analysisSkipped: boolean
  /** Null while active. */
  deactivation: AccountExplorerDeactivation | null
}

/** What is knowable about a retired main row — which is less than one would like. */
export interface AccountExplorerDeactivation {
  /**
   * The account's last *successful* mastery check. Null means the retirement was
   * never confirmed by a completed check, since a failed lookup leaves both the
   * flag and the stamp untouched.
   */
  confirmedByActivityCheckAtUtc: string | null
  /** Always false: there is no retirement-reason column. */
  reasonKnown: boolean
  /** The two causes the boolean collapses together, spelled out. Render it. */
  reasonNote: string
}

export interface AccountExplorerPositionStat {
  position: string
  games: number
  rate: number
}

/** One `rank_snapshots` row. */
export interface AccountExplorerRankSnapshot {
  capturedAtUtc: string
  tier: string
  division: string
  leaguePoints: number
  /** Null on snapshots taken before queue totals were recorded. */
  wins: number | null
  losses: number | null
}
