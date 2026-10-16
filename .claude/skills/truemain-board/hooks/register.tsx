import { atom, read, update } from 'claude-code'
import type { EngineInterface, Register } from 'claude-code'

import type { Board, BoardFilter, BoardItem } from '../types'
import { ISSUES_QUERY, ISSUES_SEARCH, ITERATIONS_QUERY, OWNER, PROJECT_NUMBER, filterTitle, parseIssuesPage, parseIterations, pickIterations, renderText, select, startPrompt } from './board'

const PANE = 'truemain-board'
const board = atom({ plugin: 'truemain-board', key: 'board' } as const, null)
const filter = atom({ plugin: 'truemain-board', key: 'filter' } as const, 'current')
const FILTERS: readonly BoardFilter[] = ['current', 'next', 'todo', 'all']
const STALE_MS = 120_000

let isLoading = false

async function load($: EngineInterface, force = false): Promise<Board | null> {
  const held = await read($, board)
  const now = await $.clock.now()
  if (!force && held !== null && now - held.fetchedAt < STALE_MS) return held
  if (isLoading) return held
  isLoading = true
  try {
    const cwd = await $.session.cwd()
    const gql = async (query: string, vars: readonly string[]) => {
      try {
        const ran = await $.process.run(['gh', 'api', 'graphql', '-f', `query=${query}`, ...vars], { cwd, timeoutMs: 60_000 })
        if (ran.exitCode !== 0) return { error: ran.stderr.trim().slice(0, 200) || `gh exited ${ran.exitCode}` }
        return { json: JSON.parse(ran.stdout) as unknown }
      } catch (error) {
        return { error: String(error).slice(0, 200) }
      }
    }

    const errors: string[] = []
    const items: BoardItem[] = []
    let after: string | null = null
    for (let page = 0; page < 6; page += 1) {
      const answer = await gql(ISSUES_QUERY, ['-f', `q=${ISSUES_SEARCH}`, ...(after === null ? [] : ['-f', `after=${after}`])])
      if ('error' in answer) {
        errors.push(`issues: ${answer.error}`)
        break
      }
      const parsed = parseIssuesPage(answer.json)
      items.push(...parsed.items)
      after = parsed.next
      if (after === null) break
    }

    const iterationsAnswer = await gql(ITERATIONS_QUERY, ['-f', `owner=${OWNER}`, '-F', `number=${PROJECT_NUMBER}`])
    const iterations = 'error' in iterationsAnswer ? [] : parseIterations(iterationsAnswer.json)
    if ('error' in iterationsAnswer) errors.push(`iterations: ${iterationsAnswer.error}`)
    const today = new Date(now).toISOString().slice(0, 10)
    const { current, next } = pickIterations(iterations, today)

    const fresh: Board = { fetchedAt: now, items, iterations, current, next, error: errors.length === 0 ? null : errors.join(' | ') }
    await update($, board, () => fresh)
    return fresh
  } finally {
    isLoading = false
  }
}

function parseFilter(arg: string): BoardFilter | null {
  const word = arg.trim().toLowerCase()
  return (FILTERS as readonly string[]).includes(word) ? (word as BoardFilter) : word === '' ? 'current' : null
}

export const register: Register = on => {
  on('session.start', async ($, e, next) => {
    await $.command.register({
      name: 'sprint',
      description: 'Open the TrueMain sprint board (Project #2): open issues of the current sprint by Status and Priority; click a row to draft "Commence la #N"',
      argumentHint: '[current|next|todo|all]',
    })
    await $.tool.register({
      name: 'sprint_issues',
      description:
        'TrueMain GitHub Project #2: the open issues planned on the current sprint (or next / unplanned todo / all), with their board Status and Priority, ordered In Progress → Todo, then P0 → P3. Use it to answer "what is on the sprint", "what should I pick next" or before proposing new work; it replaces paging gh project item-list.',
      inputSchema: {
        type: 'object',
        properties: {
          filter: { type: 'string', enum: ['current', 'next', 'todo', 'all'], description: 'Which issues (default current).' },
          refresh: { type: 'boolean', description: 'Re-query GitHub even if the last pull is under 2 minutes old.' },
        },
      },
    })
    return next(e)
  })

  on('command.run', { command: 'sprint' }, async ($, e) => {
    const chosen = parseFilter(e.args)
    if (chosen === null) return { text: `Unknown filter "${e.args.trim()}": use current, next, todo or all.` }
    await update($, filter, () => chosen)
    const loaded = await load($)
    await $.ui.open({ id: PANE, title: 'Sprint board' })
    return { text: loaded === null ? 'The board could not be loaded.' : renderText(loaded, chosen) }
  })

  on('tool.call', { tool: 'mcp__truemain-board__sprint_issues' }, async ($, e) => {
    const input = e as { filter?: unknown; refresh?: unknown }
    const chosen = parseFilter(typeof input.filter === 'string' ? input.filter : '') ?? 'current'
    const loaded = await load($, input.refresh === true)
    return { result: loaded === null ? 'The board could not be loaded.' : renderText(loaded, chosen) }
  })

  on('ui.render', { component: 'Pane', requestId: PANE }, async ($, e) => {
    const { Box, Button, Text } = $.ui.resolve(e)
    const loaded = await read($, board)
    const chosen = await read($, filter)
    const columns = e.props.bodyColumns
    if (loaded === null) {
      return (
        <Box flexDirection="column">
          <Text dimColor>Loading the board…</Text>
        </Box>
      )
    }
    const items = select(loaded, chosen)
    const rows = Math.max(3, (e.viewport?.rows ?? 30) - 6)
    const titleRoom = Math.max(8, columns - 22)
    let group: string | null | undefined
    const lines: JSX.Element[] = []
    for (const item of items.slice(0, rows)) {
      if (item.status !== group) {
        group = item.status
        lines.push(
          <Text bold key={`g-${group ?? 'none'}`}>
            {group ?? 'no Status'}
          </Text>,
        )
      }
      const title = item.title.length > titleRoom ? `${item.title.slice(0, titleRoom - 1)}…` : item.title
      lines.push(
        <Button key={`i-${item.number}`} plain onPress={() => void $.prompt.fill({ text: startPrompt(item), mode: 'replace' })}>
          <Text color="suggestion">#{item.number}</Text> <Text color={item.priority === 'P0' ? 'error' : item.priority === 'P1' ? 'warning' : 'subtle'}>{item.priority ?? '--'}</Text> {title}
        </Button>,
      )
    }
    return (
      <Box flexDirection="column" gap={0}>
        <Text dimColor wrap="truncate">
          {filterTitle(loaded, chosen)} · {items.length} issue{items.length === 1 ? '' : 's'}
          {loaded.error === null ? '' : ' · !'}
        </Text>
        <Box flexDirection="row" gap={1}>
          {FILTERS.map(name => (
            <Button key={`f-${name}`} plain variant={name === chosen ? 'primary' : 'secondary'} label={name === chosen ? `[${name}]` : name} onPress={() => update($, filter, () => name)} />
          ))}
          <Button key="refresh" plain label="↻" onPress={() => void load($, true)} />
        </Box>
        {lines}
        {items.length > rows && <Text dimColor>… {items.length - rows} more (use /sprint for the full list)</Text>}
        {items.length === 0 && <Text dimColor>Nothing here.</Text>}
      </Box>
    )
  })
}
