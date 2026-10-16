import type { Board, BoardFilter, BoardItem, BoardIteration } from '../types'

export const OWNER = 'ilyanfraimbault'
export const REPO = 'TrueMain'
export const PROJECT_NUMBER = 2

export const ISSUES_QUERY = `query($q:String!,$after:String){search(query:$q,type:ISSUE,first:100,after:$after){issueCount pageInfo{hasNextPage endCursor} nodes{... on Issue{number title state updatedAt labels(first:10){nodes{name}} projectItems(first:5){nodes{project{number} fieldValues(first:12){nodes{... on ProjectV2ItemFieldSingleSelectValue{name field{... on ProjectV2SingleSelectField{name}}} ... on ProjectV2ItemFieldIterationValue{title field{... on ProjectV2IterationField{name}}}}}}}}}}}`
export const ISSUES_SEARCH = `repo:${OWNER}/${REPO} is:issue is:open`

export const ITERATIONS_QUERY = `query($owner:String!,$number:Int!){user(login:$owner){projectV2(number:$number){field(name:"Sprint"){... on ProjectV2IterationField{configuration{iterations{title startDate duration} completedIterations{title startDate duration}}}}}}}`

type Json = Record<string, unknown>
const obj = (value: unknown): Json => (typeof value === 'object' && value !== null ? (value as Json) : {})
const str = (value: unknown, fallback = ''): string => (typeof value === 'string' ? value : fallback)
const list = (value: unknown): unknown[] => (Array.isArray(value) ? value : [])

/** One page of ISSUES_QUERY: its items and the cursor of the next page, if any. */
export function parseIssuesPage(raw: unknown): { items: BoardItem[]; next: string | null } {
  const search = obj(obj(obj(raw).data).search)
  const items: BoardItem[] = []
  for (const node of list(search.nodes)) {
    const issue = obj(node)
    if (typeof issue.number !== 'number') continue
    let status: string | null = null
    let priority: string | null = null
    let sprint: string | null = null
    let isOnBoard = false
    for (const item of list(obj(issue.projectItems).nodes)) {
      const entry = obj(item)
      if (obj(entry.project).number !== PROJECT_NUMBER) continue
      isOnBoard = true
      for (const value of list(obj(entry.fieldValues).nodes)) {
        const field = obj(value)
        const name = str(obj(field.field).name)
        if (name === 'Status') status = str(field.name) || null
        else if (name === 'Priority') priority = str(field.name) || null
        else if (name === 'Sprint') sprint = str(field.title) || null
      }
    }
    items.push({
      number: issue.number,
      title: str(issue.title),
      labels: list(obj(issue.labels).nodes).map(label => str(obj(label).name)).filter(name => name !== ''),
      updatedAt: str(issue.updatedAt),
      status,
      priority,
      sprint,
      isOnBoard,
    })
  }
  const pageInfo = obj(search.pageInfo)
  return { items, next: pageInfo.hasNextPage === true ? str(pageInfo.endCursor) || null : null }
}

/** ITERATIONS_QUERY parsed: every iteration, past and future, by start date. */
export function parseIterations(raw: unknown): BoardIteration[] {
  const configuration = obj(obj(obj(obj(obj(obj(raw).data).user).projectV2).field).configuration)
  const all = [...list(configuration.iterations), ...list(configuration.completedIterations)]
  return all
    .map(item => obj(item))
    .filter(item => typeof item.title === 'string' && typeof item.startDate === 'string')
    .map(item => ({ title: str(item.title), startDate: str(item.startDate), endDate: addDays(str(item.startDate), Number(item.duration ?? 14)) }))
    .sort((a, b) => a.startDate.localeCompare(b.startDate))
}

/** `date + days` as YYYY-MM-DD (UTC). */
export function addDays(date: string, days: number): string {
  const at = new Date(`${date}T00:00:00Z`)
  at.setUTCDate(at.getUTCDate() + days)
  return at.toISOString().slice(0, 10)
}

/** The iteration containing `today` and the first one starting after it, as project-set.sh resolves `current` and `next`. */
export function pickIterations(iterations: readonly BoardIteration[], today: string): { current: BoardIteration | null; next: BoardIteration | null } {
  const current = iterations.find(it => it.startDate <= today && today < it.endDate) ?? null
  const next = iterations.find(it => it.startDate > today) ?? null
  return { current, next }
}

const STATUS_ORDER: Record<string, number> = { 'In Progress': 0, Todo: 1, Done: 3 }
const statusRank = (status: string | null) => (status === null ? 2 : (STATUS_ORDER[status] ?? 2))
const priorityRank = (priority: string | null) => {
  const rank = priority === null ? Number.NaN : Number(priority.replace(/^P/, ''))
  return Number.isNaN(rank) ? 9 : rank
}

/** The items the filter keeps, ordered by Status (In Progress, Todo, none), then Priority, then number. */
export function select(board: Board, filter: BoardFilter): BoardItem[] {
  const kept = board.items.filter(item => {
    switch (filter) {
      case 'current':
        return board.current !== null && item.sprint === board.current.title
      case 'next':
        return board.next !== null && item.sprint === board.next.title
      case 'todo':
        return item.sprint === null && (item.status === 'Todo' || item.status === null)
      default:
        return true
    }
  })
  return kept.sort((a, b) => statusRank(a.status) - statusRank(b.status) || priorityRank(a.priority) - priorityRank(b.priority) || a.number - b.number)
}

export function filterTitle(board: Board, filter: BoardFilter): string {
  switch (filter) {
    case 'current':
      return board.current === null ? 'current sprint (none configured for today)' : `${board.current.title} (${board.current.startDate} → ${board.current.endDate})`
    case 'next':
      return board.next === null ? 'next sprint (none configured)' : `${board.next.title} (${board.next.startDate} → ${board.next.endDate})`
    case 'todo':
      return 'unplanned backlog (no sprint, Todo or no status)'
    default:
      return 'every open issue'
  }
}

/** The `/sprint` output and the sprint_issues tool's answer. */
export function renderText(board: Board, filter: BoardFilter): string {
  const items = select(board, filter)
  const lines = [`${filterTitle(board, filter)} — ${items.length} open issue${items.length === 1 ? '' : 's'}`]
  let group: string | null | undefined
  for (const item of items) {
    if (item.status !== group) {
      group = item.status
      lines.push(`\n${group ?? (item.isOnBoard ? 'no Status' : 'not on the board')}`)
    }
    lines.push(`  #${item.number} ${item.priority ?? '--'} ${item.title}${filter === 'all' && item.sprint !== null ? ` [${item.sprint}]` : ''}`)
  }
  if (items.length === 0) lines.push('  (nothing)')
  if (board.error !== null) lines.push(`\nNote: ${board.error}`)
  return lines.join('\n')
}

/** What a click on a row puts in the prompt box. */
export function startPrompt(item: BoardItem): string {
  return `Commence la #${item.number} (${item.title})`
}
