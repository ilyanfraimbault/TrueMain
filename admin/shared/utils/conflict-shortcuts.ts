// The keyboard map of the open conflict queue (#2293), in one place: the queue
// listens with it, the action buttons and the `?` overlay print it. Keys use the
// `defineShortcuts` syntax (lower-case `key`, `enter`, `escape`).
// `k` moves up the queue, so "keep" takes `h` (the column that "Holds it"): a stray
// `k` right after the next conflict opens must not settle it with no confirmation.

/** Where a shortcut applies: the queue (no compare open) or the open compare. */
export type ConflictShortcutScope = 'queue' | 'compare'

/** What the queue does on a key; the action commands name `conflictActions` keys. */
export type ConflictCommand =
  | 'next'
  | 'previous'
  | 'open'
  | 'close'
  | 'confirm'
  | 'help'
  | 'move'
  | 'keep'
  | 'detach'
  | 'merge-claimant'
  | 'merge-owner'

export interface ConflictShortcut {
  /** The `defineShortcuts` key. */
  key: string
  /** The keys as `UKbd` prints them. */
  kbds: string[]
  scope: ConflictShortcutScope
  command: ConflictCommand
  label: string
}

export const CONFLICT_SHORTCUTS: readonly ConflictShortcut[] = [
  { key: 'j', kbds: ['j'], scope: 'queue', command: 'next', label: 'Select the next conflict' },
  { key: 'k', kbds: ['k'], scope: 'queue', command: 'previous', label: 'Select the previous conflict' },
  { key: 'enter', kbds: ['enter'], scope: 'queue', command: 'open', label: 'Open the selected conflict' },
  { key: 'm', kbds: ['m'], scope: 'compare', command: 'move', label: 'Move the account to the claimant' },
  { key: 'h', kbds: ['h'], scope: 'compare', command: 'keep', label: 'Keep it with the holder' },
  { key: 'd', kbds: ['d'], scope: 'compare', command: 'detach', label: 'Detach it from both' },
  { key: '1', kbds: ['1'], scope: 'compare', command: 'merge-claimant', label: 'Merge into the left column' },
  { key: '2', kbds: ['2'], scope: 'compare', command: 'merge-owner', label: 'Merge into the right column' },
  { key: 'enter', kbds: ['enter'], scope: 'compare', command: 'confirm', label: 'Confirm the armed action' },
  { key: 'escape', kbds: ['escape'], scope: 'compare', command: 'close', label: 'Close the compare' },
  { key: '?', kbds: ['?'], scope: 'queue', command: 'help', label: 'Show these shortcuts' },
]

/** The shortcuts of one scope, in the order the overlay lists them. */
export function shortcutsIn(scope: ConflictShortcutScope): ConflictShortcut[] {
  return CONFLICT_SHORTCUTS.filter(s => s.scope === scope)
}

/** The key printed next to an action button, or null when it has none. */
export function actionKbd(actionKey: string): string | null {
  return CONFLICT_SHORTCUTS.find(s => s.scope === 'compare' && s.command === actionKey)?.kbds[0] ?? null
}

/** `j`/`k` over `length` rows: from no selection, `j` takes the first and `k` the last; clamped at both ends. */
export function stepSelection(index: number, length: number, delta: 1 | -1): number {
  if (length === 0) {
    return -1
  }
  if (index < 0 || index >= length) {
    return delta === 1 ? 0 : length - 1
  }
  return Math.min(length - 1, Math.max(0, index + delta))
}

/**
 * The conflict to open after one was settled: the row that took its place, else
 * the new last one; never the settled one, which a stale page may still list.
 */
export function nextAfterResolve<T extends { id: string }>(rows: T[], resolvedId: string, resolvedIndex: number): T | null {
  const left = rows.filter(r => r.id !== resolvedId)
  if (left.length === 0) {
    return null
  }
  return left[Math.min(Math.max(resolvedIndex, 0), left.length - 1)] ?? null
}
