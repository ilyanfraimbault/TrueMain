// Known-players management (#2019): `GET /ops/personas[/{id}]` and the operator's
// edits — `POST /ops/personas`, `PATCH|DELETE /ops/personas/{id}`, and the
// `links` / `accounts` sub-resources. "Persona" is the code's word for a known
// player; the UI says "known player".

export type PersonaListView = 'All' | 'Conflicts' | 'Unresolved' | 'Removed'

export interface PersonaListFilters {
  [key: string]: string | number | undefined
  view?: PersonaListView
  source?: string
  search?: string
  page?: number
  pageSize?: number
}

export interface PersonaListRow {
  id: string
  slug: string
  displayName: string
  /** The source that first published the known player. */
  source: string
  /** The operator set the name; the sources no longer overwrite it. */
  displayNameLocked: boolean
  createdAtUtc: string
  updatedAtUtc: string
  removedAtUtc: string | null
  /** Accounts linked to a tracked account: the ones the public page shows. */
  trackedAccounts: number
  /** Accounts no tracked account answers to yet. */
  unresolvedAccounts: number
  links: number
  openConflicts: number
}

export interface PersonasPage {
  personas: PersonaListRow[]
  total: number
  page: number
  pageSize: number
}

export interface PersonaAdminAccount {
  /** The account row, which unlink addresses. */
  id: string
  /** The Riot ID as the source published it. */
  gameName: string
  tagLine: string
  platformId: string
  source: string
  riotAccountId: string | null
  /** The tracked account's current Riot ID (it follows renames). */
  currentGameName: string | null
  currentTagLine: string | null
  createdAtUtc: string
}

export interface PersonaAdminLink {
  id: string
  kind: string
  url: string
  source: string
  createdAtUtc: string
}

export type PersonaExclusionReason = 'conflict' | 'unlinked' | 'takedown'

export interface PersonaAdminExclusion {
  gameName: string
  tagLine: string
  platformId: string
  reason: PersonaExclusionReason
  createdAtUtc: string
}

export interface PersonaAdminRemovedLink {
  url: string
  createdAtUtc: string
}

export interface PersonaAdminConflict {
  id: string
  /** `claimant`: this player claims an account another holds; `owner`: another claims one of its accounts. */
  role: 'claimant' | 'owner'
  gameName: string
  tagLine: string
  platformId: string
  otherSlug: string
  otherDisplayName: string
  source: string
  lastSeenAtUtc: string
}

export interface PersonaAdminDetail {
  id: string
  slug: string
  displayName: string
  source: string
  displayNameLocked: boolean
  createdAtUtc: string
  updatedAtUtc: string
  removedAtUtc: string | null
  mergedIntoSlug: string | null
  accounts: PersonaAdminAccount[]
  links: PersonaAdminLink[]
  exclusions: PersonaAdminExclusion[]
  removedLinks: PersonaAdminRemovedLink[]
  conflicts: PersonaAdminConflict[]
}

/** The intake's answer for one account: `linked`, `pending`, `seeded`, `untracked`, `conflict`, `excluded` or `invalid`. */
export interface PersonaAccountOutcome {
  gameName: string
  tagLine: string
  platformId: string
  status: string
}

export interface PersonaEdited {
  id: string
  slug: string
  accounts: PersonaAccountOutcome[]
}

export interface PersonaLinkInput {
  kind: string
  url: string
}

export interface PersonaAccountInput {
  gameName: string
  tagLine: string
  platformId: string
}

export interface PersonaCreateBody {
  slug: string
  displayName: string
  links?: PersonaLinkInput[]
  accounts?: PersonaAccountInput[]
}
