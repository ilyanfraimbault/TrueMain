// What the known-players conflict queue (#2253) says about a conflict and the
// four ways to settle it. Kept under `shared/` so it is importable — and tested —
// without the Nuxt runtime.
import type {
  PersonaConflict,
  PersonaConflictAccount,
  PersonaConflictPersona,
  PersonaConflictResolution,
  PersonaConflictResolveBody,
  PersonaConflictResolved,
} from '~~/shared/types/ops'

export type BadgeColor = 'primary' | 'success' | 'warning' | 'error' | 'info' | 'neutral'

/** One way to settle a conflict, as the detail panel offers it. */
export interface ConflictActionOption {
  key: string
  body: PersonaConflictResolveBody
  label: string
  /** What it does, in the operator's words — shown under the button. */
  description: string
  icon: string
  color: BadgeColor
  /** Destructive enough to ask twice (it takes the account from someone, or retires a page). */
  confirm: boolean
}

/**
 * How to name one side of a conflict: its display name, with the slug when both
 * sides carry the same name — the usual case of a duplicate (two "Peng").
 */
export function personaName(
  persona: Pick<PersonaConflictPersona, 'displayName' | 'slug'>,
  other: Pick<PersonaConflictPersona, 'displayName'>,
): string {
  return persona.displayName.toLowerCase() === other.displayName.toLowerCase()
    ? `${persona.displayName} (${persona.slug})`
    : persona.displayName
}

/** "1 account", "4 accounts". */
export function accountCount(count: number): string {
  return `${count.toLocaleString('en-US')} ${count === 1 ? 'account' : 'accounts'}`
}

/** `Name#TAG` as the source published it. */
export function riotId(account: Pick<PersonaConflictAccount, 'gameName' | 'tagLine'>): string {
  return `${account.gameName}#${account.tagLine}`
}

/**
 * The tracked account's current Riot ID when it no longer reads like the one the
 * source published (a rename), else null — the list shows it under the claim.
 */
export function renamedTo(account: PersonaConflictAccount): string | null {
  if (!account.currentGameName || account.currentTagLine === null) {
    return null
  }

  const current = `${account.currentGameName}#${account.currentTagLine}`
  return current.toLowerCase() === riotId(account).toLowerCase() ? null : current
}

export function resolutionLabel(resolution: PersonaConflictResolution | null): string {
  switch (resolution) {
    case 'Moved': return 'Moved to the claimant'
    case 'Kept': return 'Kept by the holder'
    case 'Detached': return 'Detached from both'
    case 'Merged': return 'Merged'
    default: return 'Open'
  }
}

export function resolutionColor(resolution: PersonaConflictResolution | null): BadgeColor {
  switch (resolution) {
    case 'Moved': return 'info'
    case 'Kept': return 'success'
    case 'Detached': return 'error'
    case 'Merged': return 'primary'
    default: return 'warning'
  }
}

/** The four actions, worded for this conflict's two known players. */
export function conflictActions(conflict: Pick<PersonaConflict, 'claimant' | 'owner'>): ConflictActionOption[] {
  const claimant = personaName(conflict.claimant, conflict.owner)
  const owner = personaName(conflict.owner, conflict.claimant)
  return [
    {
      key: 'move',
      body: { action: 'move' },
      label: `Move to ${claimant}`,
      description: `The account leaves ${owner}. ${owner}'s sources can no longer bring it back.`,
      icon: 'i-lucide-arrow-right-left',
      color: 'info',
      confirm: true,
    },
    {
      key: 'keep',
      body: { action: 'keep' },
      label: `Keep with ${owner}`,
      description: `Nothing moves. ${claimant}'s claim on this account is ignored from now on.`,
      icon: 'i-lucide-shield-check',
      color: 'success',
      confirm: false,
    },
    {
      key: 'detach',
      body: { action: 'detach' },
      label: 'Detach from both',
      description: `The account belongs to neither: removed from ${owner}, barred for both.`,
      icon: 'i-lucide-unlink',
      color: 'error',
      confirm: true,
    },
    {
      key: 'merge-owner',
      body: { action: 'merge', keep: 'owner' },
      label: `Merge into ${owner}`,
      description: `Same person: ${claimant}'s accounts and links join ${owner}, and ${claimant}'s page redirects there.`,
      icon: 'i-lucide-merge',
      color: 'primary',
      confirm: true,
    },
    {
      key: 'merge-claimant',
      body: { action: 'merge', keep: 'claimant' },
      label: `Merge into ${claimant}`,
      description: `Same person: ${owner}'s accounts and links join ${claimant}, and ${owner}'s page redirects there.`,
      icon: 'i-lucide-merge',
      color: 'primary',
      confirm: true,
    },
  ]
}

/** The toast after a resolution: what happened, and what else it settled. */
export function resolvedMessage(resolved: PersonaConflictResolved): { title: string, description?: string } {
  const also = resolved.alsoResolved > 0
    ? `${resolved.alsoResolved} other conflict${resolved.alsoResolved === 1 ? '' : 's'} settled with it.`
    : undefined
  switch (resolved.resolution) {
    case 'Moved':
      return { title: `Account moved to ${resolved.holderSlug}`, description: also }
    case 'Kept':
      return { title: `Account kept by ${resolved.holderSlug}`, description: also }
    case 'Detached':
      return { title: 'Account detached from both', description: also }
    default:
      return {
        title: `${resolved.mergedSlug} merged into ${resolved.holderSlug}`,
        description: [`/players/${resolved.mergedSlug} now redirects.`, also].filter(Boolean).join(' '),
      }
  }
}

/** A failed resolve, from the API's problem details when it sent one. */
export function resolveErrorMessage(error: unknown): string {
  const data = (error as { data?: { detail?: unknown, title?: unknown } } | null)?.data
  const detail = typeof data?.detail === 'string' ? data.detail : null
  const title = typeof data?.title === 'string' ? data.title : null
  return detail ?? title ?? 'The conflict could not be resolved.'
}
