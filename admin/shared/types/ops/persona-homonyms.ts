// Known players sharing a name (#2271): `POST /ops/personas/conflicts/auto-merge`,
// `GET /ops/personas/homonyms` and `POST /ops/personas/homonyms/{merge,distinct}`;
// and sharing a social channel (#2286): `GET /ops/personas/channel-conflicts`.
import type { PersonaOps } from './personas'

export interface PersonaAutoMerged {
  /** Same-name conflicts settled as merges. */
  merged: number
  /** Same-name known players sharing a social channel merged into their oldest (#2286). */
  mergedByChannel: number
  /** Other open account conflicts those merges settled too. */
  alsoResolved: number
  /** Conflicts that changed under the sweep, and channel groups holding a pair marked different: they stay. */
  skipped: number
}

/** Two live known players listing the same social channel (#2286), both in full. */
export interface PersonaChannelConflict {
  /** The one created first: the one a merge keeps by default. `disputed` is always false here. */
  older: PersonaOps
  newer: PersonaOps
  /** The channels both list; kinds lowercase. */
  sharedChannels: { kind: string, url: string }[]
}

export interface PersonaChannelConflictsResponse {
  conflicts: PersonaChannelConflict[]
  total: number
  page: number
  pageSize: number
}

export interface PersonaChannelConflictsFilters {
  [key: string]: string | number | undefined
  search?: string
  page?: number
  pageSize?: number
}

export interface PersonaHomonymGroup {
  /** The shared name key: lowercase, without spaces, dots, dashes or underscores. */
  key: string
  /** Oldest first: the one a merge keeps by default. `disputed` is always false here. */
  personas: PersonaOps[]
  /** Link keys two or more of them list — the same channel, the strongest hint of one person. */
  sharedLinks: string[]
  /** Pairs already marked as different people, each as its two persona ids. */
  distinctPairs: [string, string][]
}

export interface PersonaHomonymsResponse {
  groups: PersonaHomonymGroup[]
  total: number
  page: number
  pageSize: number
}

export interface PersonaHomonymsFilters {
  [key: string]: string | number | undefined
  search?: string
  page?: number
  pageSize?: number
}

export interface PersonaHomonymMergeBody {
  keepId: string
  mergeIds: string[]
}

export interface PersonaHomonymMerged {
  keptSlug: string
  /** The slugs that now redirect to `keptSlug`. */
  mergedSlugs: string[]
  /** Open conflicts between them the merge settled. */
  alsoResolved: number
}

export interface PersonaHomonymDistinctBody {
  personaIds: string[]
}

export interface PersonaHomonymMarked {
  /** Pairs newly marked as different people. */
  marked: number
}
