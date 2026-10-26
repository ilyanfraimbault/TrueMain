// Persona conflict queue (#2253): `GET /ops/personas/conflicts[/{id}]` and
// `POST /ops/personas/conflicts/{id}/resolve`. "Persona" is the code's word for a
// known player; the UI says "known player".

export type PersonaConflictStatus = 'Open' | 'Resolved'
export type PersonaConflictResolution = 'Moved' | 'Kept' | 'Detached' | 'Merged'
export type PersonaConflictAction = 'move' | 'keep' | 'detach' | 'merge'

export interface PersonaConflictAccount {
  /** The Riot ID as the claiming source published it. */
  gameName: string
  tagLine: string
  platformId: string
  /** The tracked account it answers to; null while TrueMain does not track it. */
  riotAccountId: string | null
  /** The tracked account's current Riot ID (it follows renames). */
  currentGameName: string | null
  currentTagLine: string | null
}

export interface PersonaConflictPersona {
  id: string
  slug: string
  displayName: string
  source: string
  /** The older of the two is the one a bulk merge keeps (#2295). */
  createdAtUtc: string
  accountCount: number
  linkCount: number
}

export interface PersonaConflict {
  id: string
  status: PersonaConflictStatus
  resolution: PersonaConflictResolution | null
  /** The source of the claim. */
  source: string
  firstSeenAtUtc: string
  lastSeenAtUtc: string
  timesSeen: number
  resolvedAtUtc: string | null
  account: PersonaConflictAccount
  /** The known player whose source lists the account. */
  claimant: PersonaConflictPersona
  /** The known player holding it. */
  owner: PersonaConflictPersona
  /** What the two players' links say about the answer (#2277); the open queue is ordered by it. */
  evidence: PersonaConflictEvidence
}

export interface PersonaConflictEvidence {
  /** 10 per shared social channel, 3 per shared reference page, −5 per social kind with different channels. */
  score: number
  /** Social kinds (twitch, youtube, x…) whose channel both list. */
  sharedSocial: string[]
  /** Reference pages (Leaguepedia, LoLPros, DeepLoL, a website) both list. */
  sharedReferences: number
  /** Social kinds both list, with different channels. */
  differingSocial: string[]
}

export interface PersonaConflictsResponse {
  conflicts: PersonaConflict[]
  total: number
  page: number
  pageSize: number
  open: number
  resolved: number
}

export interface PersonaConflictsFilters {
  [key: string]: string | number | undefined
  status?: PersonaConflictStatus
  source?: string
  search?: string
  page?: number
  pageSize?: number
}

export interface PersonaOpsAccount {
  gameName: string
  tagLine: string
  platformId: string
  source: string
  riotAccountId: string | null
  /** The account the conflict is about. */
  disputed: boolean
}

export interface PersonaOpsLink {
  kind: string
  url: string
  source: string
}

export interface PersonaOps {
  id: string
  slug: string
  displayName: string
  source: string
  createdAtUtc: string
  accounts: PersonaOpsAccount[]
  links: PersonaOpsLink[]
}

export interface PersonaConflictDetail {
  conflict: PersonaConflict
  claimant: PersonaOps
  owner: PersonaOps
}

export interface PersonaConflictResolveBody {
  action: PersonaConflictAction
  /** For `merge`: the known player that stays. */
  keep?: 'claimant' | 'owner'
}

export interface PersonaConflictResolved {
  id: string
  resolution: PersonaConflictResolution
  /** The known player holding the account now; null when detached. */
  holderSlug: string | null
  /** For a merge: the slug that now redirects to `holderSlug`. */
  mergedSlug: string | null
  /** Other open conflicts the action settled too. */
  alsoResolved: number
}

// Bulk resolution of the most certain conflicts (#2295):
// `GET /ops/personas/conflicts/bulk-resolve/preview` and `POST /ops/personas/conflicts/bulk-resolve`.

/** One conflict a bulk merge would settle, and the known player it keeps (the older). */
export interface PersonaConflictBulkCandidate {
  conflict: PersonaConflict
  keep: 'claimant' | 'owner'
  keptSlug: string
  /** The slug that redirects to `keptSlug` once merged. */
  mergedSlug: string
}

export interface PersonaConflictBulkPreview {
  minScore: number
  /** At most this many per bulk action. */
  limit: number
  /** Every open conflict qualifying at `minScore`; `conflicts` holds the first `limit`. */
  eligible: number
  conflicts: PersonaConflictBulkCandidate[]
}

export interface PersonaConflictBulkResolveBody {
  /** The ids previewed, never a query evaluated at click time. */
  ids: string[]
  minScore: number
}

export type PersonaConflictBulkSkipReason = 'notFound' | 'notOpen' | 'differingChannel' | 'belowFloor' | 'changed'

export interface PersonaConflictBulkOutcome {
  id: string
  status: 'resolved' | 'skipped'
  reason: PersonaConflictBulkSkipReason | null
  resolution: PersonaConflictResolved | null
}

export interface PersonaConflictBulkResolved {
  resolved: number
  skipped: number
  outcomes: PersonaConflictBulkOutcome[]
}
