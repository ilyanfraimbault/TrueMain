// Known players sharing a name (#2271): `POST /ops/personas/conflicts/auto-merge`,
// `GET /ops/personas/homonyms` and `POST /ops/personas/homonyms/{merge,distinct}`.
import type { PersonaOps } from './personas'

export interface PersonaAutoMerged {
  /** Same-name conflicts settled as merges. */
  merged: number
  /** Other open conflicts those merges settled too. */
  alsoResolved: number
  /** Same-name conflicts that changed under the sweep and stay in the queue. */
  skipped: number
}

export interface PersonaHomonymGroup {
  /** The shared name key: lowercase, without spaces, dots, dashes or underscores. */
  key: string
  /** Oldest first: the one a merge keeps by default. `disputed` is always false here. */
  personas: PersonaOps[]
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
