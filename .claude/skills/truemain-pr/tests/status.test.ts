import { describe, expect, test } from 'claude-code/testing'

import type { PrStatus } from '../types'
import { contextSection, headline, issueOfBranch, parseIssue, parsePr, report, verdict } from '../hooks/status'

const HEAD = '2be53664d149df27b0ba72f061d7b0d1d1ba39c4'
const OLD = '1111111111111111111111111111111111111111'

const check = (name: string, conclusion: string | null, workflowName = 'CI', status = 'COMPLETED') => ({
  __typename: 'CheckRun',
  name,
  status,
  conclusion,
  workflowName,
})

const review = (state: string, oid: string, submittedAt = '2026-10-16T10:00:00Z') => ({
  author: { login: 'claude' },
  state,
  commit: { oid },
  submittedAt,
  body: `Spec: clean — everything asked for is there.\nStandards: ${state === 'APPROVED' ? 'clean' : 'one finding'}.`,
})

const prJson = (over: Record<string, unknown> = {}) => ({
  number: 2062,
  title: 'ci: skip the SonarQube Cloud gate on release PRs',
  url: 'https://github.com/ilyanfraimbault/TrueMain/pull/2062',
  state: 'OPEN',
  isDraft: false,
  baseRefName: 'develop',
  headRefName: 'ci/2061-sonar-skip-release-prs',
  headRefOid: HEAD,
  mergeable: 'MERGEABLE',
  mergeStateStatus: 'CLEAN',
  statusCheckRollup: [
    check('Detect changes', 'SUCCESS'),
    check('Claude Review', 'SUCCESS', 'Claude Code Review'),
    check('Backend build and test', 'SKIPPED'),
    check('Quality gate', 'SUCCESS', 'SonarQube Cloud'),
    { __typename: 'StatusContext', context: 'SonarCloud Code Analysis', state: 'SUCCESS' },
  ],
  reviews: [review('APPROVED', HEAD)],
  ...over,
})

describe('issueOfBranch', () => {
  test('reads the issue number off a conventional branch only', () => {
    expect(issueOfBranch('feat/526-rank-scoped-builds')).toBe(526)
    expect(issueOfBranch('ci/2061-sonar-skip-release-prs')).toBe(2061)
    expect(issueOfBranch('mods-workflow-analysis-9efee6')).toBe(null)
    expect(issueOfBranch('develop')).toBe(null)
    expect(issueOfBranch(null)).toBe(null)
  })
})

describe('parsePr', () => {
  test('keeps the Claude Review check apart from CI and finds the bot verdict', () => {
    const pr = parsePr(prJson())
    expect(pr.ci).toBe('success')
    expect(pr.reviewCheck).toBe('success')
    expect(pr.checks.map(c => c.name)).not.toContain('Claude Review')
    expect(pr.review?.state).toBe('APPROVED')
    expect(pr.review?.isCurrent).toBe(true)
    expect(verdict(pr)).toEqual({ isReady: true, blockers: [] })
  })

  test('a failing check beats pending, pending beats success', () => {
    const failing = parsePr(prJson({ statusCheckRollup: [check('Backend build and test', 'FAILURE'), check('File sizes', null, 'CI', 'IN_PROGRESS')] }))
    expect(failing.ci).toBe('failure')
    expect(verdict(failing).blockers[0]).toMatch(/CI failing: Backend build and test/)
    const pending = parsePr(prJson({ statusCheckRollup: [check('Detect changes', 'SUCCESS'), check('File sizes', null, 'CI', 'QUEUED')] }))
    expect(pending.ci).toBe('pending')
    expect(verdict(pending).isReady).toBe(false)
  })

  test('a verdict for an older commit is stale, and the latest bot review wins', () => {
    const pr = parsePr(prJson({ reviews: [review('CHANGES_REQUESTED', OLD, '2026-10-16T09:00:00Z'), review('APPROVED', OLD, '2026-10-16T09:30:00Z')] }))
    expect(pr.review?.state).toBe('APPROVED')
    expect(pr.review?.isCurrent).toBe(false)
    const { isReady, blockers } = verdict(pr)
    expect(isReady).toBe(false)
    expect(blockers[0]).toMatch(/is for 1111111, HEAD is 2be5366/)
  })

  test('human reviews are ignored, changes requested on HEAD block, conflicts block', () => {
    const human = parsePr(prJson({ reviews: [{ author: { login: 'ilyanfraimbault' }, state: 'APPROVED', commit: { oid: HEAD }, submittedAt: '2026-10-16T10:00:00Z', body: '' }] }))
    expect(human.review).toBe(null)
    expect(verdict(human).blockers).toContain('no Claude review verdict yet')
    const findings = parsePr(prJson({ reviews: [review('CHANGES_REQUESTED', HEAD)] }))
    expect(verdict(findings).blockers[0]).toMatch(/CHANGES_REQUESTED on HEAD/)
    const dirty = parsePr(prJson({ mergeable: 'CONFLICTING', mergeStateStatus: 'DIRTY' }))
    expect(verdict(dirty).blockers).toContain('merge conflicts with the base branch')
  })

  test('a docs-only PR with no CI check can still be ready', () => {
    const docs = parsePr(prJson({ statusCheckRollup: [check('Claude Review', 'SUCCESS', 'Claude Code Review')] }))
    expect(docs.ci).toBe('none')
    expect(verdict(docs).isReady).toBe(true)
  })

  test('merged, draft and no-PR states', () => {
    expect(verdict(parsePr(prJson({ state: 'MERGED' }))).blockers).toEqual(['PR is merged'])
    expect(verdict(parsePr(prJson({ isDraft: true }))).blockers).toContain('PR is a draft')
    expect(verdict(null).isReady).toBe(false)
  })
})

describe('parseIssue', () => {
  const answer = {
    data: {
      repository: {
        issue: {
          number: 2061,
          title: 'ci: release PRs should skip the Sonar gate',
          state: 'OPEN',
          projectItems: {
            nodes: [
              {
                project: { number: 2 },
                fieldValues: {
                  nodes: [
                    {},
                    { name: 'In Progress', field: { name: 'Status' } },
                    { name: 'P1', field: { name: 'Priority' } },
                    { title: 'Sprint 8', field: { name: 'Sprint' } },
                  ],
                },
              },
            ],
          },
        },
      },
    },
  }

  test('reads title, state and the three board fields', () => {
    expect(parseIssue(answer)).toEqual({ number: 2061, title: 'ci: release PRs should skip the Sonar gate', state: 'OPEN', status: 'In Progress', sprint: 'Sprint 8', priority: 'P1' })
    expect(parseIssue({ data: { repository: { issue: null } } })).toBe(null)
    expect(parseIssue({ errors: [{ message: 'Could not resolve' }] })).toBe(null)
  })
})

describe('report, headline and the prompt section', () => {
  const status: PrStatus = {
    branch: 'ci/2061-sonar-skip-release-prs',
    issue: { number: 2061, title: 'ci: release PRs should skip the Sonar gate', state: 'OPEN', status: 'In Progress', sprint: 'Sprint 8', priority: 'P1' },
    pr: parsePr(prJson()),
    updatedAt: 0,
    error: null,
  }

  test('the report names the done-criteria and the merge command', () => {
    const text = report(status)
    expect(text).toMatch(/Issue: #2061 .* board: In Progress · P1 · Sprint 8/)
    expect(text).toMatch(/Claude review: APPROVED for 2be5366 \(current HEAD\)/)
    expect(text).toMatch(/Done-criteria: met — merge now .*gh pr merge 2062 --squash --delete-branch/)
    expect(headline(status)).toBe('PR #2062 · CI ✓ · review ✓ · ready to merge')
  })

  test('the prompt section carries stable facts only', () => {
    const text = contextSection(status) ?? ''
    expect(text).toMatch(/tracks issue #2061/)
    expect(text).toMatch(/PR #2062/)
    expect(text).not.toMatch(/✓|✗|ready to merge|2be5366|APPROVED/)
    expect(contextSection({ ...status, issue: null, pr: null })).toBe(null)
    expect(contextSection({ ...status, branch: null })).toBe(null)
  })
})
