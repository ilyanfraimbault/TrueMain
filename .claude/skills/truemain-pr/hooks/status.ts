import type { PrCheck, PrCheckOutcome, PrInfo, PrIssue, PrReview, PrStatus, PrVerdict } from '../types'

export const REVIEW_BOT = 'claude'
export const REVIEW_CHECK_NAME = 'Claude Review'
export const PROJECT_NUMBER = 2

/** The issue number a `<type>/<issue>-<slug>` branch tracks. */
export function issueOfBranch(branch: string | null): number | null {
  const match = branch === null ? null : /^[a-z]+\/(\d+)-/.exec(branch)
  return match === null ? null : Number(match[1])
}

export const PR_JSON_FIELDS = 'number,title,url,state,isDraft,baseRefName,headRefName,headRefOid,mergeable,mergeStateStatus,statusCheckRollup,reviews'

type Json = Record<string, unknown>
const obj = (value: unknown): Json => (typeof value === 'object' && value !== null ? (value as Json) : {})
const str = (value: unknown, fallback = ''): string => (typeof value === 'string' ? value : fallback)
const list = (value: unknown): unknown[] => (Array.isArray(value) ? value : [])

/** `gh pr view --json <PR_JSON_FIELDS>` parsed into the band's model. */
export function parsePr(raw: unknown): PrInfo {
  const pr = obj(raw)
  const headSha = str(pr.headRefOid)

  const checks: PrCheck[] = []
  let reviewCheck: PrCheckOutcome | null = null
  for (const item of list(pr.statusCheckRollup)) {
    const check = obj(item)
    const name = str(check.name, str(check.context, '?'))
    const workflow = str(check.workflowName)
    const outcome = outcomeOf(check)
    if (name === REVIEW_CHECK_NAME) {
      reviewCheck = outcome
      continue
    }
    checks.push({ name, workflow, outcome })
  }

  let review: PrReview | null = null
  for (const item of list(pr.reviews)) {
    const entry = obj(item)
    if (str(obj(entry.author).login) !== REVIEW_BOT) continue
    const submittedAt = str(entry.submittedAt)
    if (review !== null && submittedAt < review.submittedAt) continue
    const sha = str(obj(entry.commit).oid)
    review = {
      state: str(entry.state, 'COMMENTED'),
      sha,
      isCurrent: sha !== '' && sha === headSha,
      summary: str(entry.body).replace(/\s+/g, ' ').trim().slice(0, 240),
      submittedAt,
    }
  }

  const state = str(pr.state, 'OPEN')
  return {
    number: Number(pr.number ?? 0),
    title: str(pr.title),
    url: str(pr.url),
    state: state === 'MERGED' || state === 'CLOSED' ? state : 'OPEN',
    isDraft: pr.isDraft === true,
    base: str(pr.baseRefName),
    head: str(pr.headRefName),
    headSha,
    mergeable: str(pr.mergeable, 'UNKNOWN'),
    mergeState: str(pr.mergeStateStatus, 'UNKNOWN'),
    ci: ciOf(checks),
    checks,
    reviewCheck,
    review,
  }
}

function outcomeOf(check: Json): PrCheckOutcome {
  if (check.__typename === 'StatusContext') {
    const state = str(check.state)
    if (state === 'SUCCESS') return 'success'
    if (state === 'PENDING' || state === 'EXPECTED') return 'pending'
    return 'failure'
  }
  if (str(check.status) !== 'COMPLETED') return 'pending'
  const conclusion = str(check.conclusion)
  if (conclusion === 'SUCCESS') return 'success'
  if (conclusion === 'SKIPPED' || conclusion === 'NEUTRAL') return 'skipped'
  return 'failure'
}

function ciOf(checks: readonly PrCheck[]): PrInfo['ci'] {
  if (checks.some(check => check.outcome === 'failure')) return 'failure'
  if (checks.some(check => check.outcome === 'pending')) return 'pending'
  if (checks.some(check => check.outcome === 'success')) return 'success'
  return 'none'
}

export const ISSUE_QUERY = `query($owner:String!,$repo:String!,$n:Int!){repository(owner:$owner,name:$repo){issue(number:$n){number title state projectItems(first:5){nodes{project{number} fieldValues(first:12){nodes{... on ProjectV2ItemFieldSingleSelectValue{name field{... on ProjectV2SingleSelectField{name}}} ... on ProjectV2ItemFieldIterationValue{title field{... on ProjectV2IterationField{name}}}}}}}}}}`

/** The `gh api graphql` answer to ISSUE_QUERY parsed into the band's model. */
export function parseIssue(raw: unknown): PrIssue | null {
  const issue = obj(obj(obj(obj(raw).data).repository).issue)
  if (typeof issue.number !== 'number') return null
  let status: string | null = null
  let sprint: string | null = null
  let priority: string | null = null
  for (const item of list(obj(issue.projectItems).nodes)) {
    const node = obj(item)
    if (obj(node.project).number !== PROJECT_NUMBER) continue
    for (const value of list(obj(node.fieldValues).nodes)) {
      const field = obj(value)
      const fieldName = str(obj(field.field).name)
      if (fieldName === 'Status') status = str(field.name) || null
      else if (fieldName === 'Priority') priority = str(field.name) || null
      else if (fieldName === 'Sprint') sprint = str(field.title) || null
    }
  }
  return {
    number: issue.number,
    title: str(issue.title),
    state: str(issue.state) === 'CLOSED' ? 'CLOSED' : 'OPEN',
    status,
    sprint,
    priority,
  }
}

const short = (sha: string) => sha.slice(0, 7)

/** The ship skill's done-criteria, evaluated on the PR's current HEAD. */
export function verdict(pr: PrInfo | null): PrVerdict {
  if (pr === null) return { isReady: false, blockers: ['no PR for this branch'] }
  const blockers: string[] = []
  if (pr.state !== 'OPEN') return { isReady: false, blockers: [`PR is ${pr.state.toLowerCase()}`] }
  if (pr.isDraft) blockers.push('PR is a draft')
  if (pr.ci === 'failure') {
    const failing = pr.checks.filter(check => check.outcome === 'failure').map(check => check.name)
    blockers.push(`CI failing: ${failing.join(', ')}`)
  } else if (pr.ci === 'pending') {
    const pending = pr.checks.filter(check => check.outcome === 'pending').map(check => check.name)
    blockers.push(`CI running: ${pending.join(', ')}`)
  }
  if (pr.review === null) {
    blockers.push(pr.reviewCheck === 'pending' ? 'Claude review running' : 'no Claude review verdict yet')
  } else if (!pr.review.isCurrent) {
    blockers.push(`Claude review verdict (${pr.review.state}) is for ${short(pr.review.sha)}, HEAD is ${short(pr.headSha)} — wait for the re-review`)
  } else if (pr.review.state !== 'APPROVED') {
    blockers.push(`Claude review ${pr.review.state} on HEAD — fix or rebut each finding in a PR comment`)
  }
  if (pr.mergeable === 'CONFLICTING' || pr.mergeState === 'DIRTY') blockers.push('merge conflicts with the base branch')
  if (blockers.length === 0 && pr.mergeState === 'BLOCKED') blockers.push('blocked by branch protection (a required check is missing or pending)')
  return { isReady: blockers.length === 0, blockers }
}

/** The one-line summary the status line and the band share. */
export function headline(status: PrStatus): string {
  const parts: string[] = []
  if (status.pr !== null) {
    const pr = status.pr
    parts.push(`PR #${pr.number}${pr.isDraft ? ' (draft)' : ''}${pr.state !== 'OPEN' ? ` ${pr.state.toLowerCase()}` : ''}`)
    parts.push(`CI ${glyph(pr.ci)}`)
    parts.push(`review ${reviewGlyph(pr)}`)
    if (pr.state === 'OPEN' && verdict(pr).isReady) parts.push('ready to merge')
  } else if (status.issue !== null) {
    parts.push(`#${status.issue.number}${status.issue.status === null ? '' : ` ${status.issue.status}`}`)
  }
  return parts.join(' · ')
}

export function glyph(ci: PrInfo['ci'] | PrCheckOutcome): string {
  switch (ci) {
    case 'success':
      return '✓'
    case 'failure':
      return '✗'
    case 'pending':
      return '…'
    default:
      return '–'
  }
}

export function reviewGlyph(pr: PrInfo): string {
  if (pr.review === null) return pr.reviewCheck === 'pending' ? '…' : '–'
  if (!pr.review.isCurrent) return `stale (${short(pr.review.sha)})`
  return pr.review.state === 'APPROVED' ? '✓' : `✗ ${pr.review.state.toLowerCase().replace('_', ' ')}`
}

/** The `/pr` report: what the band shows, in full, for the person and the model. */
export function report(status: PrStatus): string {
  const lines: string[] = []
  lines.push(`Branch: ${status.branch ?? '(detached)'}`)
  if (status.issue !== null) {
    const issue = status.issue
    const board = [issue.status, issue.priority, issue.sprint].filter(part => part !== null).join(' · ')
    lines.push(`Issue: #${issue.number} ${issue.title} [${issue.state.toLowerCase()}${board === '' ? ', not on the board' : `; board: ${board}`}]`)
  } else if (status.branch !== null) {
    lines.push('Issue: none (the branch name carries no issue number)')
  }
  if (status.pr === null) {
    lines.push('PR: none open for this branch')
  } else {
    const pr = status.pr
    lines.push(`PR: #${pr.number} ${pr.title} — ${pr.url}`)
    lines.push(`  ${pr.head} → ${pr.base} · ${pr.state.toLowerCase()}${pr.isDraft ? ' · draft' : ''} · HEAD ${short(pr.headSha)} · mergeable ${pr.mergeable.toLowerCase()} (${pr.mergeState.toLowerCase()})`)
    const shown = pr.checks.filter(check => check.outcome !== 'skipped')
    lines.push(`  CI: ${pr.ci}${shown.length === 0 ? ' (no check ran — expected for a docs-only PR)' : ''}`)
    for (const check of shown.filter(check => check.outcome !== 'success')) {
      lines.push(`    ${glyph(check.outcome)} ${check.workflow === '' ? '' : `${check.workflow} / `}${check.name}`)
    }
    if (pr.review === null) {
      lines.push(`  Claude review: ${pr.reviewCheck === 'pending' ? 'running' : 'no verdict yet'}`)
    } else {
      lines.push(`  Claude review: ${pr.review.state} for ${short(pr.review.sha)} (${pr.review.isCurrent ? 'current HEAD' : 'STALE — HEAD moved'})`)
      if (pr.review.summary !== '') lines.push(`    ${pr.review.summary}`)
    }
    const done = verdict(pr)
    lines.push(done.isReady ? `Done-criteria: met — merge now (ship skill §5: gh pr merge ${pr.number} ${pr.base === 'master' ? '--merge' : '--squash --delete-branch'})` : `Done-criteria: not met`)
    for (const blocker of done.blockers) lines.push(`  - ${blocker}`)
  }
  if (status.error !== null) lines.push(`Note: ${status.error}`)
  return lines.join('\n')
}

/** The system-prompt section: stable facts only, so the prompt cache survives CI churn. */
export function contextSection(status: PrStatus): string | null {
  if (status.branch === null || (status.issue === null && status.pr === null)) return null
  const lines = [`# TrueMain branch context (truemain-pr mod)`, `Current branch: ${status.branch}.`]
  if (status.issue !== null) {
    const issue = status.issue
    lines.push(`It tracks issue #${issue.number} "${issue.title}" (${issue.state.toLowerCase()}${issue.status === null ? '' : `, board Status: ${issue.status}`}${issue.sprint === null ? '' : `, ${issue.sprint}`}).`)
  }
  if (status.pr !== null) {
    lines.push(`PR #${status.pr.number} "${status.pr.title}" (${status.pr.state.toLowerCase()}, base ${status.pr.base}) is open for it: ${status.pr.url}.`)
    lines.push('For its live CI, Claude review verdict (checked against HEAD) and the ship done-criteria call the pr_status tool; do not poll gh pr checks/view in a loop.')
  }
  return lines.join('\n')
}
