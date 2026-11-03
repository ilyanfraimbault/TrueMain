import { describe, expect, it } from 'vitest'
import { actionKbd, CONFLICT_SHORTCUTS, nextAfterResolve, shortcutsIn, stepSelection } from '~~/shared/utils/conflict-shortcuts'
import { conflictActions } from '~~/shared/utils/persona-conflicts'
import { conflict } from './fixtures'

describe('CONFLICT_SHORTCUTS', () => {
  it('binds every conflict action to exactly one key', () => {
    const keys = conflictActions(conflict()).map(a => actionKbd(a.key))
    expect(keys).toEqual(['m', 'h', 'd', '2', '1'])
    expect(actionKbd('auto-merge')).toBeNull()
  })

  it('never binds one key twice in a scope, and keeps the queue keys off the actions', () => {
    for (const scope of ['queue', 'compare'] as const) {
      const keys = shortcutsIn(scope).map(s => s.key)
      expect(new Set(keys).size).toBe(keys.length)
    }
    const queueKeys = new Set(shortcutsIn('queue').map(s => s.key))
    const actionKeys = CONFLICT_SHORTCUTS.filter(s => s.scope === 'compare' && !['confirm', 'close'].includes(s.command))
    expect(actionKeys.filter(s => queueKeys.has(s.key))).toEqual([])
  })
})

describe('stepSelection', () => {
  it('starts at an end, then clamps', () => {
    expect(stepSelection(-1, 3, 1)).toBe(0)
    expect(stepSelection(-1, 3, -1)).toBe(2)
    expect(stepSelection(0, 3, 1)).toBe(1)
    expect(stepSelection(2, 3, 1)).toBe(2)
    expect(stepSelection(0, 3, -1)).toBe(0)
    expect(stepSelection(5, 3, 1)).toBe(0)
    expect(stepSelection(1, 0, 1)).toBe(-1)
  })
})

describe('nextAfterResolve', () => {
  const rows = [{ id: 'a' }, { id: 'b' }, { id: 'c' }]

  it('takes the row that slid into the settled one\'s place', () => {
    expect(nextAfterResolve(rows.filter(r => r.id !== 'b'), 'b', 1)).toEqual({ id: 'c' })
    expect(nextAfterResolve(rows, 'b', 1)).toEqual({ id: 'c' })
  })

  it('falls back to the last row, and to nothing on an empty queue', () => {
    expect(nextAfterResolve([{ id: 'a' }, { id: 'b' }], 'c', 2)).toEqual({ id: 'b' })
    expect(nextAfterResolve([{ id: 'a' }], 'a', 0)).toBeNull()
    expect(nextAfterResolve([{ id: 'a' }], 'x', -1)).toEqual({ id: 'a' })
  })
})
