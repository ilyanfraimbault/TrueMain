// Why a conflict sits where it does in the open queue (#2277): the links its two
// known players share — a social channel above all — or the social kinds they
// list with different channels.
import type { BadgeColor, PersonaConflictEvidence } from '~~/shared/types/ops'

export interface EvidenceBadge {
  label: string
  color: BadgeColor
}

export function evidenceBadges(evidence: PersonaConflictEvidence): EvidenceBadge[] {
  const badges: EvidenceBadge[] = []
  if (evidence.sharedSocial.length > 0) {
    badges.push({ label: `same ${evidence.sharedSocial.join(', ')}`, color: 'success' })
  }
  if (evidence.sharedReferences > 0) {
    badges.push({
      label: `${evidence.sharedReferences} shared page${evidence.sharedReferences === 1 ? '' : 's'}`,
      color: evidence.sharedSocial.length > 0 ? 'neutral' : 'info',
    })
  }
  if (evidence.differingSocial.length > 0) {
    badges.push({ label: `different ${evidence.differingSocial.join(', ')}`, color: 'warning' })
  }
  return badges.length > 0 ? badges : [{ label: 'nothing in common', color: 'neutral' }]
}
