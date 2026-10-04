// Account seed requests — `/api/ops/accounts/seed`.

/**
 * Lifecycle of a seed request (`POST /api/ops/accounts/seed`):
 *   Pending   — accepted, not yet picked up
 *   Resolving — resolving the Riot ID → PUUID / account
 *   Ingested  — account + mastery-derived candidates created and queued. NOTE:
 *               actual match ingestion + main classification happen on the next
 *               Ingestor cycle, NOT synchronously here.
 *   Failed    — resolution/queueing failed; see `error`.
 */
export type SeedRequestStatus = 'Pending' | 'Resolving' | 'Ingested' | 'Failed'

/** A status that will not change on its own — polling can stop. */
export const TERMINAL_SEED_STATUSES: readonly SeedRequestStatus[] = ['Ingested', 'Failed']

/**
 * `GET /api/ops/accounts/seed/{id}` and one row of
 * `GET /api/ops/accounts/seed`. Resolved identifiers are `null` until the
 * request reaches `Ingested`.
 */
export interface SeedRequestReadModel {
  id: string
  gameName: string
  tagLine: string
  platformId: string
  status: SeedRequestStatus
  error: string | null
  requestedAtUtc: string
  processedAtUtc: string | null
  resolvedPuuid: string | null
  resolvedRiotAccountId: string | null
}

/** Body for `POST /api/ops/accounts/seed`. */
export interface SeedAccountBody {
  gameName: string
  tagLine: string
  /** PlatformId, e.g. `EUW1` / `KR` / `NA1`. */
  platformId: string
}

/** `202` response of `POST /api/ops/accounts/seed`. */
export interface SeedAccountResponse {
  id: string
  status: SeedRequestStatus
  /**
   * `true` when this call created a new seed request; `false` when an existing
   * (still-unprocessed) request for the same Riot ID + platform was returned
   * idempotently — i.e. the account was already queued/seeded.
   */
  created: boolean
}

/**
 * `GET /api/ops/accounts/seed` — one page of seed requests, newest-first, with the
 * total matching the same filters so the panel can render a pager (#1166).
 *
 * Shaped like `CandidatesResponse` deliberately: both lists sit on `/candidates`
 * and page identically.
 */
export interface SeedRequestsResponse {
  requests: SeedRequestReadModel[]
  /** Total rows matching the filters (across all pages). */
  total: number
  page: number
  pageSize: number
}

/** Filters for `GET /api/ops/accounts/seed`. Empty/undefined = no filter. */
export interface SeedRequestsFilters {
  /** A `SeedRequestStatus` name. */
  status?: SeedRequestStatus
  /** Case-insensitive substring match on the Riot ID (gameName/tagLine). */
  search?: string
  /** PlatformId, e.g. `EUW1` / `KR` / `NA1`. A value the backend cannot parse is a 400. */
  region?: string
  /** 1-based page index. */
  page?: number
  /** Rows per page; backend clamps to [1, 100], default 25. */
  pageSize?: number
}
