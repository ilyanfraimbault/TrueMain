import { atom, read, update } from 'claude-code'
import type { EngineInterface, Register } from 'claude-code'

import type { PrStatus } from '../types'
import { ISSUE_QUERY, PR_JSON_FIELDS, contextSection, glyph, headline, issueOfBranch, parseIssue, parsePr, report, reviewGlyph, verdict } from './status'

const status = atom({ plugin: 'truemain-pr', key: 'status' } as const, null)
const isHidden = atom({ plugin: 'truemain-pr', key: 'isHidden' } as const, false)

const OWNER = 'ilyanfraimbault'
const REPO = 'TrueMain'
const HOT_MS = 60_000
const COLD_MS = 300_000
const TICK_MS = 30_000
const REFRESH_AFTER = /\b(git\s+push|gh\s+pr|gh\s+issue|project-set\.sh|git\s+(checkout|switch|branch\s+-m|commit|merge|reset|rebase))\b/

let isRefreshing = false

async function refresh($: EngineInterface): Promise<void> {
    if (isRefreshing) return
    isRefreshing = true
    try {
      const cwd = await $.session.cwd()
      const run = async (argv: readonly string[]) => {
        try {
          return await $.process.run(argv, { cwd, timeoutMs: 30_000 })
        } catch {
          return null
        }
      }

      const errors: string[] = []
      const now = await $.clock.now()
      const head = await run(['git', 'rev-parse', '--abbrev-ref', 'HEAD'])
      const branch = head !== null && head.exitCode === 0 && head.stdout.trim() !== 'HEAD' && head.stdout.trim() !== '' ? head.stdout.trim() : null
      if (branch === null) {
        await update($, status, () => ({ branch: null, issue: null, pr: null, updatedAt: now, error: null }))
        return
      }

      const issueNumber = issueOfBranch(branch)
      const [prRun, issueRun] = await Promise.all([
        run(['gh', 'pr', 'view', '--json', PR_JSON_FIELDS]),
        issueNumber === null
          ? Promise.resolve(null)
          : run(['gh', 'api', 'graphql', '-f', `query=${ISSUE_QUERY}`, '-f', `owner=${OWNER}`, '-f', `repo=${REPO}`, '-F', `n=${issueNumber}`]),
      ])

      let pr: PrStatus['pr'] = null
      if (prRun !== null && prRun.exitCode === 0) {
        try {
          pr = parsePr(JSON.parse(prRun.stdout))
        } catch (error) {
          errors.push(`gh pr view answered something unexpected: ${String(error)}`)
        }
      } else if (prRun !== null && !/no pull requests found/i.test(prRun.stderr)) {
        errors.push(`gh pr view failed: ${prRun.stderr.trim().slice(0, 160)}`)
      }

      let issue: PrStatus['issue'] = null
      if (issueRun !== null && issueRun.exitCode === 0) {
        try {
          issue = parseIssue(JSON.parse(issueRun.stdout))
        } catch (error) {
          errors.push(`issue query answered something unexpected: ${String(error)}`)
        }
      } else if (issueRun !== null) {
        errors.push(`issue #${issueNumber} lookup failed: ${issueRun.stderr.trim().slice(0, 160)}`)
      }

      const previous = await read($, status)
      const next: PrStatus = { branch, issue, pr, updatedAt: await $.clock.now(), error: errors.length === 0 ? null : errors.join(' | ') }
      await update($, status, () => next)
      $.ui.status(headline(next) || undefined)
      announce($, previous, next)
    } finally {
      isRefreshing = false
    }
}

function announce($: EngineInterface, before: PrStatus | null, after: PrStatus): void {
    const pr = after.pr
    if (pr === null || pr.state !== 'OPEN') return
    const was = before?.pr !== null && before?.pr !== undefined && before.pr.number === pr.number ? before.pr : null
    if (was !== null && was.ci !== pr.ci && (pr.ci === 'success' || pr.ci === 'failure')) {
      const failing = pr.checks.filter(check => check.outcome === 'failure').map(check => check.name)
      $.ui.toast(pr.ci === 'success' ? `PR #${pr.number}: CI green` : `PR #${pr.number}: CI failed (${failing.join(', ')})`, { timeoutMs: 8000 })
    }
    const reviewKey = pr.review === null ? null : `${pr.review.sha}:${pr.review.state}`
    const wasReviewKey = was?.review === null || was?.review === undefined ? null : `${was.review.sha}:${was.review.state}`
    if (was !== null && reviewKey !== null && reviewKey !== wasReviewKey && pr.review?.isCurrent) {
      $.ui.toast(`PR #${pr.number}: Claude review ${pr.review.state} on HEAD`, { timeoutMs: 8000 })
    }
    const ready = verdict(pr).isReady
    if (ready && (was === null || !verdict(was).isReady)) {
      $.ui.toast(`PR #${pr.number} meets the done-criteria — merge it (ship skill §5)`, { timeoutMs: 10_000 })
    }
}

export const register: Register = on => {
  on('session.start', async ($, e, next) => {
    await $.command.register({
      name: 'pr',
      description: 'Branch → issue → PR status: CI, Claude review vs HEAD, ship done-criteria (hide|show to toggle the band)',
      argumentHint: '[hide|show]',
    })
    await $.tool.register({
      name: 'pr_status',
      description:
        'TrueMain: the current branch → its issue (board Status/Priority/Sprint) → its open PR with CI, the Claude review verdict checked against HEAD, mergeability and the ship skill done-criteria. Call it instead of polling gh pr checks/view in a loop; `refresh: false` answers from the last poll (at most 1–5 min old).',
      inputSchema: { type: 'object', properties: { refresh: { type: 'boolean', description: 'Poll GitHub now (default true).' } } },
      isDeferred: false,
    })
    void refresh($)
    $.clock.every(TICK_MS, async () => {
      const current = await read($, status)
      const age = (await $.clock.now()) - (current?.updatedAt ?? 0)
      const isHot = current?.pr !== null && current?.pr !== undefined && current.pr.state === 'OPEN' && !verdict(current.pr).isReady
      if (age >= (isHot ? HOT_MS : COLD_MS)) void refresh($)
    })
    return next(e)
  })

  on('command.run', { command: 'pr' }, async ($, e) => {
    const arg = e.args.trim()
    if (arg === 'hide' || arg === 'show') {
      await update($, isHidden, () => arg === 'hide')
      return { text: arg === 'hide' ? 'PR band hidden (/pr show brings it back).' : 'PR band shown.' }
    }
    await refresh($)
    const current = await read($, status)
    return { text: current === null ? 'No status yet.' : report(current) }
  })

  on('tool.call', { tool: 'mcp__truemain-pr__pr_status' }, async ($, e) => {
    if ((e as { refresh?: unknown }).refresh !== false) await refresh($)
    const current = await read($, status)
    return { result: current === null ? 'No status yet.' : report(current) }
  })

  on('tool.call', { tool: 'Bash' }, async ($, e, next) => {
    const ran = await next(e)
    if (REFRESH_AFTER.test(e.command)) $.clock.after(3000, () => void refresh($))
    return ran
  })

  on('prompt.compose', async ($, e, next) => {
    const composed = await next(e)
    const current = await read($, status)
    const text = current === null ? null : contextSection(current)
    if (text === null) return composed
    return { sections: [...composed.sections, { id: 'truemain-pr:context', text, scope: 'session' as const }] }
  })

  on('ui.render', { component: 'AbovePrompt' }, async ($, e, next) => {
    const current = await read($, status)
    if (e.props.hasSurvey || current === null || current.branch === null || (await read($, isHidden))) return next(e)
    const { Box, Button, Text } = $.ui.resolve(e)
    const pr = current.pr
    const issue = current.issue
    const columns = e.props.bodyColumns
    const fixed = 12 + current.branch.length + (pr === null ? 0 : 44)
    const issueRoom = Math.max(0, columns - fixed - (issue === null ? 0 : 10 + (issue.status?.length ?? 0)))
    const issueTitle = issue === null ? '' : issue.title.length > issueRoom ? `${issue.title.slice(0, Math.max(0, issueRoom - 1))}…` : issue.title
    const done = pr === null ? null : verdict(pr)
    const ciColor = pr === null ? 'subtle' : pr.ci === 'success' ? 'success' : pr.ci === 'failure' ? 'error' : pr.ci === 'pending' ? 'warning' : 'subtle'
    const reviewColor =
      pr === null || pr.review === null ? 'subtle' : !pr.review.isCurrent ? 'warning' : pr.review.state === 'APPROVED' ? 'success' : 'error'

    return (
      <Box flexDirection="row" gap={1}>
        <Text bold>⎇ {current.branch}</Text>
        {issue !== null && (
          <Text dimColor wrap="truncate">
            #{issue.number} {issueTitle}
            {issue.status === null ? '' : ` [${issue.status}]`}
          </Text>
        )}
        {pr !== null && (
          <Text>
            PR #{pr.number}
            {pr.isDraft ? ' draft' : ''}
            {pr.state !== 'OPEN' ? ` ${pr.state.toLowerCase()}` : ''}
          </Text>
        )}
        {pr !== null && <Text color={ciColor}>CI {glyph(pr.ci)}</Text>}
        {pr !== null && <Text color={reviewColor}>review {reviewGlyph(pr)}</Text>}
        {pr !== null && (pr.mergeable === 'CONFLICTING' || pr.mergeState === 'DIRTY') && <Text color="error">conflicts</Text>}
        {done !== null && done.isReady && (
          <Text color="success" bold>
            → ready to merge
          </Text>
        )}
        {current.error !== null && <Text color="warning">!</Text>}
        <Button key="refresh" plain label="↻" onPress={() => void refresh($)} />
        <Button key="hide" plain label="hide" onPress={() => update($, isHidden, () => true)} />
      </Box>
    )
  })
}
