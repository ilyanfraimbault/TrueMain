// The bulk resolution of the most certain conflicts (#2295): the floors the operator
// picks from, how the preview names each merge, and what the API's outcomes say.
// Kept under `shared/` so it is importable — and tested — without the Nuxt runtime.
import type {
  PersonaConflictBulkCandidate,
  PersonaConflictBulkResolved,
  PersonaConflictBulkSkipReason,
} from '~~/shared/types/ops'
import { personaName } from './persona-conflicts'

/** The score floors offered: 10 per shared social channel, so each step is about one more channel. */
export const BULK_FLOORS: readonly { value: number, label: string }[] = [
  { value: 10, label: 'Score ≥ 10' },
  { value: 20, label: 'Score ≥ 20' },
  { value: 30, label: 'Score ≥ 30' },
]

export const DEFAULT_BULK_FLOOR = 20

/** "Peng (dpm) into Peng": the known player that goes, then the one that stays. */
export function bulkMergeLine(candidate: PersonaConflictBulkCandidate): { merged: string, kept: string } {
  const { claimant, owner } = candidate.conflict
  const keptIsClaimant = candidate.keep === 'claimant'
  const kept = keptIsClaimant ? claimant : owner
  const merged = keptIsClaimant ? owner : claimant
  return { merged: personaName(merged, kept), kept: personaName(kept, merged) }
}

export function skipReasonLabel(reason: PersonaConflictBulkSkipReason | null): string {
  switch (reason) {
    case 'notFound': return 'No longer exists'
    case 'notOpen': return 'Already resolved (by hand, or by an earlier merge of this batch)'
    case 'differingChannel': return 'Now lists a differing channel'
    case 'belowFloor': return 'No longer reaches the floor'
    case 'changed': return 'One of the known players changed since the preview'
    default: return 'Skipped'
  }
}

/** "3 conflicts merged", with the skips when there were some. */
export function bulkResolvedMessage(result: PersonaConflictBulkResolved): { title: string, description?: string } {
  const title = `${result.resolved} conflict${result.resolved === 1 ? '' : 's'} merged`
  const also = result.outcomes.reduce((sum, o) => sum + (o.resolution?.alsoResolved ?? 0), 0)
  const parts = [
    result.skipped > 0 ? `${result.skipped} skipped: they changed since the preview.` : null,
    also > 0 ? `${also} other conflict${also === 1 ? '' : 's'} settled with them.` : null,
  ].filter(Boolean)
  return { title, description: parts.length > 0 ? parts.join(' ') : undefined }
}
