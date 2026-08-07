// Badge presentation for the intake pipeline's two status enums, shared by every
// panel that shows one. Colors trace New → Validated/Rejected and Pending →
// Ingested/Failed, and stay close to the Overview candidate-pipeline palette —
// so a status never looks like two different things on two pages.
import type { BadgeColor, MainCandidateStatus, SeedRequestStatus } from '~~/shared/types/ops'

export const CANDIDATE_STATUSES: MainCandidateStatus[] = [
  'New',
  'Scored',
  'Queued',
  'Processing',
  'Validated',
  'Rejected',
]

export const CANDIDATE_STATUS_COLOR: Record<MainCandidateStatus, BadgeColor> = {
  New: 'neutral',
  Scored: 'info',
  Queued: 'warning',
  Processing: 'warning',
  Validated: 'success',
  Rejected: 'error',
}

export const CANDIDATE_STATUS_ICON: Record<MainCandidateStatus, string> = {
  New: 'i-lucide-sparkles',
  Scored: 'i-lucide-calculator',
  Queued: 'i-lucide-list-ordered',
  Processing: 'i-lucide-loader',
  Validated: 'i-lucide-circle-check',
  Rejected: 'i-lucide-circle-x',
}

export function candidateStatusColor(status: MainCandidateStatus): BadgeColor {
  return CANDIDATE_STATUS_COLOR[status] ?? 'neutral'
}

export function candidateStatusIcon(status: MainCandidateStatus): string {
  return CANDIDATE_STATUS_ICON[status] ?? 'i-lucide-circle'
}

export const SEED_STATUSES: SeedRequestStatus[] = ['Pending', 'Resolving', 'Ingested', 'Failed']

export const SEED_STATUS_COLOR: Record<SeedRequestStatus, BadgeColor> = {
  Pending: 'neutral',
  Resolving: 'info',
  Ingested: 'success',
  Failed: 'error',
}

export const SEED_STATUS_ICON: Record<SeedRequestStatus, string> = {
  Pending: 'i-lucide-clock',
  Resolving: 'i-lucide-loader',
  Ingested: 'i-lucide-circle-check',
  Failed: 'i-lucide-circle-x',
}

export function seedStatusColor(status: SeedRequestStatus): BadgeColor {
  return SEED_STATUS_COLOR[status] ?? 'neutral'
}

export function seedStatusIcon(status: SeedRequestStatus): string {
  return SEED_STATUS_ICON[status] ?? 'i-lucide-circle'
}
