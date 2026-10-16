export type BoardItem = {
  number: number
  title: string
  labels: string[]
  updatedAt: string
  /** Project #2 fields; null when the issue is not on the board or the field is unset. */
  status: string | null
  priority: string | null
  sprint: string | null
  isOnBoard: boolean
}

export type BoardIteration = { title: string; startDate: string; endDate: string }

export type BoardFilter = 'current' | 'next' | 'todo' | 'all'

export type Board = {
  fetchedAt: number
  /** Every open issue of the repository. */
  items: BoardItem[]
  iterations: BoardIteration[]
  current: BoardIteration | null
  next: BoardIteration | null
  error: string | null
}

declare module 'claude-code' {
  interface PluginState {
    'truemain-board': { board: Board | null; filter: BoardFilter }
  }
}
