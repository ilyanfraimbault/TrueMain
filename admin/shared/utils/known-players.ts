// What the known-players management (#2019) says about a known player and the
// operator's edits. Kept under `shared/` so it is importable — and tested —
// without the Nuxt runtime.
import type { PersonaAdminAccount, PersonaExclusionReason, PersonaListRow, PersonaListView } from '~~/shared/types/ops'
import { TRACKED_REGIONS } from '~~/shared/utils/regions'
import { riotIdError, splitRiotId } from '~~/shared/utils/riot-id'

/** Mirrors `PersonaConfiguration.SlugMaxLength` / `DisplayNameMaxLength` on the backend. */
export const SLUG_MAX_LENGTH = 64
export const DISPLAY_NAME_MAX_LENGTH = 64

/** The sources a known player, account or link can come from (`PersonaSource`). */
export const PERSONA_SOURCES = ['Leaguepedia', 'TrackingThePros', 'DeepLol', 'Dpm', 'LoLPros', 'Manual'] as const

/** The link kinds the intake stores (`PersonaLinkKind`), lowercase as the API answers them. */
export const LINK_KINDS: readonly string[] = [
  'twitch', 'youtube', 'x', 'kick', 'tiktok', 'instagram', 'soop', 'chzzk',
  'leaguepedia', 'lolpros', 'deeplol', 'website',
]

/**
 * Every Riot platform a source may list an account on — the tracked ones first;
 * the backend's `PlatformRoute`.
 * An account on another platform is stored, never seeded (#2134).
 */
export const ACCOUNT_PLATFORMS: string[] = [
  ...TRACKED_REGIONS,
  'BR1', 'EUN1', 'JP1', 'LA1', 'LA2', 'OC1', 'PH2', 'RU', 'SG2', 'TH2', 'TR1', 'TW2', 'VN2',
]

export const VIEW_ITEMS: { label: string, value: PersonaListView }[] = [
  { label: 'All known players', value: 'All' },
  { label: 'In a conflict', value: 'Conflicts' },
  { label: 'Untracked accounts', value: 'Unresolved' },
  { label: 'Removed', value: 'Removed' },
]

/** The URL key a name suggests: lowercase kebab, accents dropped, at most 64 characters. */
export function slugify(name: string): string {
  return name
    .normalize('NFD')
    .replace(/[̀-ͯ]/g, '')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
    .slice(0, SLUG_MAX_LENGTH)
    .replace(/-+$/, '')
}

/** Why `slug` is not a valid URL key, or null when it is — the backend's rule. */
export function slugError(slug: string): string | null {
  if (!slug) {
    return 'A slug is required'
  }
  if (slug.length > SLUG_MAX_LENGTH) {
    return `At most ${SLUG_MAX_LENGTH} characters`
  }
  return /^[a-z0-9]+(?:-[a-z0-9]+)*$/.test(slug) ? null : 'Lowercase letters, digits and single dashes only'
}

/** Why `name` is not a valid display name, or null when it is. */
export function displayNameError(name: string): string | null {
  const trimmed = name.trim()
  if (!trimmed) {
    return 'A name is required'
  }
  return trimmed.length > DISPLAY_NAME_MAX_LENGTH ? `At most ${DISPLAY_NAME_MAX_LENGTH} characters` : null
}

/** The account body for a typed `Name#TAG` on a platform, or the reason it is not one. */
export function accountInput(riotId: string, platformId: string):
  { gameName: string, tagLine: string, platformId: string } | { error: string } {
  const error = riotIdError(riotId)
  if (error) {
    return { error }
  }
  const { gameName, tagLine } = splitRiotId(riotId)
  return { gameName, tagLine, platformId }
}

/** Why `url` is not a link the intake stores, or null when it is (absolute http/https). */
export function linkUrlError(url: string): string | null {
  try {
    const parsed = new URL(url.trim())
    return parsed.protocol === 'https:' || parsed.protocol === 'http:' ? null : 'An http(s) URL'
  }
  catch {
    return 'An absolute URL, https://…'
  }
}

/** `Name#TAG` as the source published it, with the current Riot ID when the account was renamed. */
export function accountLabel(account: Pick<PersonaAdminAccount, 'gameName' | 'tagLine' | 'currentGameName' | 'currentTagLine'>): string {
  const published = `${account.gameName}#${account.tagLine}`
  if (!account.currentGameName || account.currentTagLine === null) {
    return published
  }
  const current = `${account.currentGameName}#${account.currentTagLine}`
  return current.toLowerCase() === published.toLowerCase() ? published : `${published} → now ${current}`
}

/** "2 tracked · 1 untracked", or "No account". */
export function accountsSummary(row: Pick<PersonaListRow, 'trackedAccounts' | 'unresolvedAccounts'>): string {
  const parts = [
    row.trackedAccounts > 0 ? `${row.trackedAccounts} tracked` : null,
    row.unresolvedAccounts > 0 ? `${row.unresolvedAccounts} untracked` : null,
  ].filter(Boolean)
  return parts.length > 0 ? parts.join(' · ') : 'No account'
}

export function exclusionReasonLabel(reason: PersonaExclusionReason): string {
  switch (reason) {
    case 'conflict': return 'lost in a conflict'
    case 'takedown': return 'taken down'
    default: return 'unlinked by an operator'
  }
}

/**
 * The toast after an account was added by hand, from the intake's answer:
 * `success` when it is on the player now, `warning` when it is not (yet).
 */
export function accountOutcomeMessage(status: string): { tone: 'success' | 'warning', title: string, description?: string } {
  switch (status) {
    case 'linked':
      return { tone: 'success', title: 'Account linked', description: 'It shows on the player\'s page.' }
    case 'seeded':
      return { tone: 'success', title: 'Account added', description: 'Not tracked yet: queued for ingestion, linked once ingested.' }
    case 'pending':
      return { tone: 'success', title: 'Account added', description: 'Not tracked yet: linked once ingested.' }
    case 'untracked':
      return { tone: 'success', title: 'Account added', description: 'Its platform is not ingested, so it does not show.' }
    case 'conflict':
      return { tone: 'warning', title: 'Not added: another known player holds it', description: 'A conflict was opened; settle it in Open conflicts.' }
    case 'excluded':
      return { tone: 'warning', title: 'Not added: taken down', description: 'A takedown bars this account from every known player.' }
    default:
      return { tone: 'warning', title: 'Not added', description: 'The Riot ID or platform is not valid.' }
  }
}

/** A failed edit, from the API's problem details when it sent one. */
export function editErrorMessage(error: unknown, fallback = 'The change was not saved.'): string {
  const data = (error as { data?: { detail?: unknown, title?: unknown, errors?: unknown } } | null)?.data
  if (typeof data?.detail === 'string' && data.detail) {
    return data.detail
  }
  if (typeof data?.title === 'string' && data.title) {
    return data.title
  }
  return fallback
}

/** The settle link for one of the player's open conflicts. */
export function conflictHref(id: string): string {
  return `/known-players?status=open&conflict=${encodeURIComponent(id)}`
}
