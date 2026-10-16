import { describe, expect, test } from 'claude-code/testing'

import type { Board } from '../types'
import { addDays, parseIssuesPage, parseIterations, pickIterations, renderText, select, startPrompt } from '../hooks/board'

const issue = (number: number, title: string, fields: { status?: string; priority?: string; sprint?: string } | null) => ({
  number,
  title,
  state: 'OPEN',
  updatedAt: '2026-10-15T00:00:00Z',
  labels: { nodes: [{ name: 'frontend' }] },
  projectItems: {
    nodes:
      fields === null
        ? []
        : [
            {
              project: { number: 2 },
              fieldValues: {
                nodes: [
                  {},
                  ...(fields.status === undefined ? [] : [{ name: fields.status, field: { name: 'Status' } }]),
                  ...(fields.priority === undefined ? [] : [{ name: fields.priority, field: { name: 'Priority' } }]),
                  ...(fields.sprint === undefined ? [] : [{ title: fields.sprint, field: { name: 'Sprint' } }]),
                ],
              },
            },
          ],
  },
})

const page = (nodes: unknown[], endCursor: string | null) => ({
  data: { search: { issueCount: nodes.length, pageInfo: { hasNextPage: endCursor !== null, endCursor }, nodes } },
})

const iterations = {
  data: {
    user: {
      projectV2: {
        field: {
          configuration: {
            iterations: [
              { title: 'Sprint 8', startDate: '2026-10-05', duration: 14 },
              { title: 'Sprint 9', startDate: '2026-10-19', duration: 14 },
            ],
            completedIterations: [{ title: 'Sprint 7', startDate: '2026-09-21', duration: 14 }],
          },
        },
      },
    },
  },
}

describe('parsing', () => {
  test('issues carry their board fields and the next cursor', () => {
    const parsed = parseIssuesPage(page([issue(2016, 'feat: known players', { status: 'In Progress', priority: 'P1', sprint: 'Sprint 8' }), issue(99, 'orphan', null)], 'abc'))
    expect(parsed.next).toBe('abc')
    expect(parsed.items[0]).toEqual({ number: 2016, title: 'feat: known players', labels: ['frontend'], updatedAt: '2026-10-15T00:00:00Z', status: 'In Progress', priority: 'P1', sprint: 'Sprint 8', isOnBoard: true })
    expect(parsed.items[1]?.isOnBoard).toBe(false)
    expect(parseIssuesPage(page([], null)).next).toBe(null)
  })

  test('iterations are sorted and dated, current and next resolved by today', () => {
    const all = parseIterations(iterations)
    expect(all.map(it => it.title)).toEqual(['Sprint 7', 'Sprint 8', 'Sprint 9'])
    expect(all[1]?.endDate).toBe('2026-10-19')
    expect(pickIterations(all, '2026-10-16')).toEqual({ current: all[1], next: all[2] })
    expect(pickIterations(all, '2026-10-19')).toEqual({ current: all[2], next: null })
    expect(pickIterations(all, '2027-01-01')).toEqual({ current: null, next: null })
    expect(addDays('2026-12-30', 3)).toBe('2027-01-02')
  })
})

describe('selection and text', () => {
  const all = parseIterations(iterations)
  const board: Board = {
    fetchedAt: 0,
    iterations: all,
    current: all[1] ?? null,
    next: all[2] ?? null,
    error: null,
    items: parseIssuesPage(
      page(
        [
          issue(10, 'todo p2 on sprint', { status: 'Todo', priority: 'P2', sprint: 'Sprint 8' }),
          issue(11, 'in progress p1', { status: 'In Progress', priority: 'P1', sprint: 'Sprint 8' }),
          issue(12, 'todo p0 on sprint', { status: 'Todo', priority: 'P0', sprint: 'Sprint 8' }),
          issue(13, 'next sprint', { status: 'Todo', priority: 'P2', sprint: 'Sprint 9' }),
          issue(14, 'backlog', { status: 'Todo', priority: 'P3' }),
          issue(15, 'not on board', null),
        ],
        null,
      ),
    ).items,
  }

  test('current sprint ordered In Progress first, then by priority', () => {
    expect(select(board, 'current').map(item => item.number)).toEqual([11, 12, 10])
    expect(select(board, 'next').map(item => item.number)).toEqual([13])
    expect(select(board, 'todo').map(item => item.number)).toEqual([14, 15])
    expect(select(board, 'all').length).toBe(6)
  })

  test('the text groups by status and names the iteration', () => {
    const text = renderText(board, 'current')
    expect(text).toMatch(/^Sprint 8 \(2026-10-05 → 2026-10-19\) — 3 open issues/)
    expect(text).toMatch(/In Progress\n  #11 P1 in progress p1\n\nTodo\n  #12 P0 todo p0 on sprint\n  #10 P2 todo p2 on sprint/)
    expect(renderText({ ...board, current: null }, 'current')).toMatch(/none configured for today/)
    expect(renderText(board, 'todo')).toMatch(/not on the board\n  #15 -- not on board/)
  })

  test('a row click drafts the start-issue prompt in French', () => {
    expect(startPrompt(board.items[1]!)).toBe('Commence la #11 (in progress p1)')
  })
})
