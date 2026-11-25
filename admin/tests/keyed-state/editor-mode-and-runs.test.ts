import type { ProcessRunReadModel } from '#site-shared/contract/api'
import { describe, expect, it } from 'vitest'
import { panelPhase } from '~/composables/useLastGoodPayload'
import { modeFor, settleSaving, startSaving, VIEW_MODE } from '~/utils/persona-editor-mode'
import { liveRun } from '~/utils/process-chain'

describe('persona editor mode', () => {
  const renaming = { s: 'renaming' as const, personaId: 'a', draft: 'Caps (G2)' }

  it('reads another player\'s rename as view', () => {
    expect(modeFor(renaming, 'b')).toEqual(VIEW_MODE)
    expect(modeFor(renaming, 'a')).toBe(renaming)
  })

  it('reopens a failed rename with its draft, and closes it on success', () => {
    const saving = startSaving(renaming, 'a', 'rename')
    expect(saving).toEqual({ s: 'saving', personaId: 'a', action: 'rename', draft: 'Caps (G2)' })
    expect(settleSaving(saving, 'a', false)).toEqual(renaming)
    expect(settleSaving(saving, 'a', true)).toEqual(VIEW_MODE)
  })

  it('closes the rename after any other edit, failed or not', () => {
    expect(settleSaving(startSaving(renaming, 'a', 'remove'), 'a', true)).toEqual(VIEW_MODE)
    expect(settleSaving(startSaving(renaming, 'a', 'unlinkAccount'), 'a', false)).toEqual(VIEW_MODE)
  })

  it('never carries a rename to another player, nor lets a late answer rewrite the mode', () => {
    expect(startSaving(renaming, 'b', 'addLink')).toEqual({ s: 'saving', personaId: 'b', action: 'addLink', draft: null })
    const elsewhere = { s: 'renaming' as const, personaId: 'b', draft: 'Faker' }
    expect(settleSaving(elsewhere, 'a', false)).toBe(elsewhere)
  })
})

describe('liveRun', () => {
  const run = (over: Partial<ProcessRunReadModel>) => ({ id: 1, processName: 'persona-scraper', status: 'Running', ...over }) as ProcessRunReadModel

  it('reads the clicked run back from the live feeds', () => {
    const clicked = run({})
    const settled = run({ status: 'Succeeded' })
    expect(liveRun(clicked, [[run({ id: 2 })], [settled]])).toBe(settled)
  })

  it('falls back to the click\'s snapshot once the run left every feed', () => {
    const clicked = run({})
    expect(liveRun(clicked, [[run({ id: 2 })]])).toBe(clicked)
    expect(liveRun(null, [[clicked]])).toBeNull()
  })
})

describe('panelPhase', () => {
  it('keeps kept data drawn when the refresh failed', () => {
    expect(panelPhase({ hasData: true, pending: false, error: new Error('x') })).toBe('stale')
    expect(panelPhase({ hasData: true, pending: true, error: null })).toBe('ready')
  })

  it('tells a cold load, an empty answer and a failure with nothing kept apart', () => {
    expect(panelPhase({ hasData: false, pending: true, error: null })).toBe('loading')
    expect(panelPhase({ hasData: false, pending: false, error: null })).toBe('empty')
    expect(panelPhase({ hasData: false, pending: true, error: new Error('x') })).toBe('failed')
  })
})
