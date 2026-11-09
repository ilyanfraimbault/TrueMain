// Badge presentation for the intake pipeline's two status enums, shared by every
// panel that shows one. Colors trace New → Validated and Pending →
// Ingested/Failed, and stay close to the Overview candidate-pipeline palette —
// so a status never looks like two different things on two pages.
import type { MainCandidateStatus, SeedRequestStatus } from '#site-shared/contract/api'
import type { BadgeColor } from '~~/shared/utils/badge-color'
import { mainCandidateStatusValues, seedRequestStatusValues } from '#site-shared/contract/api'

/** The candidate statuses in pipeline order, New → Validated, as the API declares them. */
export const CANDIDATE_STATUSES = mainCandidateStatusValues

export const CANDIDATE_STATUS_COLOR: Record<MainCandidateStatus, BadgeColor> = {
  New: 'neutral',
  Scored: 'info',
  Queued: 'warning',
  Processing: 'warning',
  Validated: 'success',
}

export const CANDIDATE_STATUS_ICON: Record<MainCandidateStatus, string> = {
  New: 'i-lucide-sparkles',
  Scored: 'i-lucide-calculator',
  Queued: 'i-lucide-list-ordered',
  Processing: 'i-lucide-loader',
  Validated: 'i-lucide-circle-check',
}

export function candidateStatusColor(status: MainCandidateStatus): BadgeColor {
  return CANDIDATE_STATUS_COLOR[status] ?? 'neutral'
}

export function candidateStatusIcon(status: MainCandidateStatus): string {
  return CANDIDATE_STATUS_ICON[status] ?? 'i-lucide-circle'
}

/** The seed-request statuses in lifecycle order, as the API declares them. */
export const SEED_STATUSES = seedRequestStatusValues

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
