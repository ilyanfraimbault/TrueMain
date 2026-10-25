// The two known players of a conflict, lined up (#2275): the operator decides by
// comparing them, so what both list sits on one row — an account by its Riot ID,
// a link by its channel — and what differs is said, not left to be spotted.
import type { PersonaOps, PersonaOpsAccount, PersonaOpsLink } from '~~/shared/types/ops'

/** `same`: both list it; `differs`: one kind of link, two channels; `only`: one side lists it. */
export type CompareMatch = 'same' | 'differs' | 'only'

export interface AccountCompareRow {
  key: string
  left: PersonaOpsAccount | null
  right: PersonaOpsAccount | null
  disputed: boolean
  match: CompareMatch
}

export interface LinkCompareRow {
  key: string
  kind: string
  left: PersonaOpsLink | null
  right: PersonaOpsLink | null
  match: CompareMatch
}

export interface CompareSummary {
  /** Links both list. */
  sameLinks: number
  /** Link kinds both have, with different channels. */
  differingKinds: string[]
  /** Accounts both list, the disputed one aside. */
  sameAccounts: number
}

const MATCH_ORDER: Record<CompareMatch, number> = { same: 0, differs: 1, only: 2 }

const accountKey = (a: PersonaOpsAccount) => `${a.platformId}:${a.gameName.toLowerCase()}#${a.tagLine.toLowerCase()}`

/**
 * A channel's identity across sources: decoded (one source escapes `성훈`, another
 * does not), lowercased, without the scheme, a `www.`, a query or a trailing slash.
 */
export function channelKey(url: string): string {
  let decoded = url
  try {
    decoded = decodeURI(url)
  }
  catch {
    // A malformed escape: compare the URL as it is.
  }
  return decoded.toLowerCase().replace(/^https?:\/\/(www\.)?/, '').replace(/[?#].*$/, '').replace(/\/+$/, '')
}

export function compareAccounts(left: PersonaOps, right: PersonaOps): AccountCompareRow[] {
  const rows = new Map<string, AccountCompareRow>()
  for (const [side, persona] of [['left', left], ['right', right]] as const) {
    for (const account of persona.accounts) {
      const key = accountKey(account)
      const row = rows.get(key) ?? { key, left: null, right: null, disputed: false, match: 'only' as CompareMatch }
      row[side] = account
      row.disputed ||= account.disputed
      row.match = row.left && row.right ? 'same' : 'only'
      rows.set(key, row)
    }
  }
  return [...rows.values()].sort((a, b) =>
    Number(b.disputed) - Number(a.disputed) || MATCH_ORDER[a.match] - MATCH_ORDER[b.match] || a.key.localeCompare(b.key))
}

export function compareLinks(left: PersonaOps, right: PersonaOps): LinkCompareRow[] {
  const kinds = [...new Set([...left.links, ...right.links].map(l => l.kind))]
  const rows: LinkCompareRow[] = []
  for (const kind of kinds) {
    const lefts = left.links.filter(l => l.kind === kind)
    const rights = right.links.filter(l => l.kind === kind)
    const unmatchedRights = [...rights]
    const unmatchedLefts: PersonaOpsLink[] = []
    for (const link of lefts) {
      const at = unmatchedRights.findIndex(r => channelKey(r.url) === channelKey(link.url))
      if (at >= 0) {
        rows.push({ key: `${kind}:${channelKey(link.url)}`, kind, left: link, right: unmatchedRights[at]!, match: 'same' })
        unmatchedRights.splice(at, 1)
      }
      else {
        unmatchedLefts.push(link)
      }
    }
    // Same kind, different channels: side by side, so the difference reads on one line.
    const paired = Math.max(unmatchedLefts.length, unmatchedRights.length)
    for (let i = 0; i < paired; i++) {
      const l = unmatchedLefts[i] ?? null
      const r = unmatchedRights[i] ?? null
      rows.push({ key: `${kind}:${i}:${l?.url ?? ''}|${r?.url ?? ''}`, kind, left: l, right: r, match: l && r ? 'differs' : 'only' })
    }
  }
  return rows.sort((a, b) => MATCH_ORDER[a.match] - MATCH_ORDER[b.match] || a.kind.localeCompare(b.kind))
}

export function compareSummary(accounts: AccountCompareRow[], links: LinkCompareRow[]): CompareSummary {
  return {
    sameLinks: links.filter(l => l.match === 'same').length,
    differingKinds: [...new Set(links.filter(l => l.match === 'differs').map(l => l.kind))],
    sameAccounts: accounts.filter(a => a.match === 'same' && !a.disputed).length,
  }
}

/** The line above the actions: what the two have in common, and what tells them apart. */
export function compareVerdict(summary: CompareSummary, links: LinkCompareRow[]): { text: string, tone: 'success' | 'warning' | 'neutral' } {
  const total = links.length
  if (total === 0) {
    return { text: 'Neither lists a link to compare.', tone: 'neutral' }
  }
  const parts = [`${summary.sameLinks} of ${total} link${total === 1 ? '' : 's'} identical`]
  if (summary.differingKinds.length > 0) {
    parts.push(`different ${summary.differingKinds.join(', ')}`)
  }
  if (summary.sameAccounts > 0) {
    parts.push(`${summary.sameAccounts} other account${summary.sameAccounts === 1 ? '' : 's'} in common`)
  }
  // One shared channel outweighs a differing one: sources often list a second channel, or another wiki page.
  const tone = summary.sameLinks > 0 ? 'success' : summary.differingKinds.length > 0 ? 'warning' : 'neutral'
  return { text: parts.join(' · '), tone }
}
