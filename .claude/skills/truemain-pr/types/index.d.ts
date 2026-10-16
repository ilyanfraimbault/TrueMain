export type PrIssue = {
  number: number
  title: string
  state: 'OPEN' | 'CLOSED'
  /** Project #2 Status field, when the issue is on the board. */
  status: string | null
  sprint: string | null
  priority: string | null
}

export type PrCheckOutcome = 'success' | 'failure' | 'pending' | 'skipped'

export type PrCheck = { name: string; workflow: string; outcome: PrCheckOutcome }

export type PrReview = {
  /** APPROVED, CHANGES_REQUESTED or COMMENTED. */
  state: string
  /** The commit the verdict was given for. */
  sha: string
  /** Whether that commit is the PR's current HEAD. */
  isCurrent: boolean
  summary: string
  submittedAt: string
}

export type PrInfo = {
  number: number
  title: string
  url: string
  state: 'OPEN' | 'MERGED' | 'CLOSED'
  isDraft: boolean
  base: string
  head: string
  headSha: string
  mergeable: string
  mergeState: string
  /** CI without the Claude review check: failure beats pending beats success; none when no check ran. */
  ci: 'success' | 'failure' | 'pending' | 'none'
  checks: PrCheck[]
  /** The Claude Code Review workflow's own check run, kept apart from CI. */
  reviewCheck: PrCheckOutcome | null
  /** The latest formal review by the Claude review bot. */
  review: PrReview | null
}

export type PrStatus = {
  branch: string | null
  issue: PrIssue | null
  pr: PrInfo | null
  updatedAt: number
  error: string | null
}

export type PrVerdict = { isReady: boolean; blockers: string[] }

declare module 'claude-code' {
  interface PluginState {
    'truemain-pr': { status: PrStatus | null; isHidden: boolean }
  }
}
