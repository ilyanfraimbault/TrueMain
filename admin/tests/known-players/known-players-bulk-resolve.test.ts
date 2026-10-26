import type { PersonaConflictBulkCandidate, PersonaConflictBulkPreview } from '~~/shared/types/ops'
import { flushPromises, mount } from '@vue/test-utils'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, h } from 'vue'
import KnownPlayersBulkResolve from '~~/app/components/known-players/KnownPlayersBulkResolve.vue'
import { bulkMergeLine, bulkResolvedMessage, skipReasonLabel } from '~~/shared/utils/persona-conflict-bulk'
import { conflict } from './fixtures'

const stub = (tag: string) => defineComponent({
  props: ['label', 'title'],
  setup(props, { slots, attrs }) {
    return () => h(tag, attrs, [String(props.label ?? props.title ?? ''), slots.body?.(), slots.default?.(), slots.footer?.()])
  },
})

const ui = {
  UModal: stub('div'),
  USelect: stub('span'),
  UBadge: stub('mark'),
  UButton: stub('button'),
  FetchErrorAlert: stub('aside'),
}

const evidence = { score: 20, sharedSocial: ['twitch', 'x'], sharedReferences: 0, differingSocial: [] }

function candidate(over: Partial<PersonaConflictBulkCandidate> = {}): PersonaConflictBulkCandidate {
  return { conflict: conflict({ evidence }), keep: 'owner', keptSlug: 'peng', mergedSlug: 'peng-dpm', ...over }
}

function preview(conflicts: PersonaConflictBulkCandidate[], eligible = conflicts.length): PersonaConflictBulkPreview {
  return { minScore: 20, limit: 100, eligible, conflicts }
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('persona-conflict-bulk', () => {
  it('names the merge from the side the preview keeps', () => {
    expect(bulkMergeLine(candidate())).toEqual({ merged: 'Peng (dpm)', kept: 'Peng' })
    expect(bulkMergeLine(candidate({ keep: 'claimant' }))).toEqual({ merged: 'Peng', kept: 'Peng (dpm)' })
  })

  it('words every skip reason and the toast', () => {
    expect(skipReasonLabel('differingChannel')).toBe('Now lists a differing channel')
    expect(skipReasonLabel('notOpen')).toContain('Already resolved')
    expect(skipReasonLabel('notFound')).toBe('No longer exists')
    expect(skipReasonLabel('belowFloor')).toBe('No longer reaches the floor')
    expect(skipReasonLabel('changed')).toContain('changed since the preview')
    expect(skipReasonLabel(null)).toBe('Skipped')

    const resolved = { id: 'c1', resolution: 'Merged' as const, holderSlug: 'peng', mergedSlug: 'peng-dpm', alsoResolved: 2 }
    expect(bulkResolvedMessage({ resolved: 1, skipped: 0, outcomes: [{ id: 'c1', status: 'resolved', reason: null, resolution: resolved }] }))
      .toEqual({ title: '1 conflict merged', description: '2 other conflicts settled with them.' })
    expect(bulkResolvedMessage({ resolved: 0, skipped: 2, outcomes: [] }))
      .toEqual({ title: '0 conflicts merged', description: '2 skipped: they changed since the preview.' })
    expect(bulkResolvedMessage({ resolved: 3, skipped: 0, outcomes: [] })).toEqual({ title: '3 conflicts merged', description: undefined })
  })
})

describe('KnownPlayersBulkResolve', () => {
  function setup(previewAnswer: Promise<PersonaConflictBulkPreview>, bulk = vi.fn()) {
    const toast = { success: vi.fn(), warning: vi.fn(), failure: vi.fn() }
    const previewBulkResolve = vi.fn().mockReturnValue(previewAnswer)
    vi.stubGlobal('previewBulkResolve', previewBulkResolve)
    vi.stubGlobal('bulkResolvePersonaConflicts', bulk)
    vi.stubGlobal('useActionToast', () => toast)
    const wrapper = mount(KnownPlayersBulkResolve, { props: { open: true }, global: { components: ui } })
    return { wrapper, toast, previewBulkResolve }
  }

  it('previews the batch, asks twice, sends exactly the previewed ids and shows the outcomes', async () => {
    const second = candidate({ conflict: conflict({ id: 'c2', evidence, account: { ...conflict().account, gameName: 'Other' } }) })
    const bulk = vi.fn().mockResolvedValue({
      resolved: 1,
      skipped: 1,
      outcomes: [
        { id: 'c1', status: 'resolved', reason: null, resolution: { id: 'c1', resolution: 'Merged', holderSlug: 'peng', mergedSlug: 'peng-dpm', alsoResolved: 0 } },
        { id: 'c2', status: 'skipped', reason: 'differingChannel', resolution: null },
      ],
    })
    const { wrapper, toast, previewBulkResolve } = setup(Promise.resolve(preview([candidate(), second], 140)), bulk)
    await flushPromises()

    expect(previewBulkResolve).toHaveBeenCalledWith(20)
    expect(wrapper.findAll('[data-candidate]')).toHaveLength(2)
    expect(wrapper.find('[data-summary]').text()).toContain('140 conflicts qualify')
    expect(wrapper.find('[data-summary]').text()).toContain('first 2')
    expect(wrapper.text()).toContain('Merge Peng (dpm) into Peng')

    const confirm = wrapper.find('[data-action="bulk-confirm"]')
    expect(confirm.text()).toBe('Merge 2 conflicts')
    await confirm.trigger('click')
    expect(bulk).not.toHaveBeenCalled()
    expect(wrapper.find('[data-action="bulk-confirm"]').text()).toBe('Confirm: merge 2 conflicts')

    await wrapper.find('[data-action="bulk-confirm"]').trigger('click')
    await flushPromises()
    expect(bulk).toHaveBeenCalledWith({ ids: ['c1', 'c2'], minScore: 20 })
    expect(toast.success).toHaveBeenCalledWith('1 conflict merged', '1 skipped: they changed since the preview.')
    expect(wrapper.emitted('resolved')).toHaveLength(1)
    const outcomes = wrapper.findAll('[data-outcome]')
    expect(outcomes.map(o => o.attributes('data-status'))).toEqual(['resolved', 'skipped'])
    expect(outcomes[0]!.text()).toContain('Merged into peng')
    expect(outcomes[1]!.text()).toContain('Now lists a differing channel')
    expect(wrapper.find('[data-action="bulk-confirm"]').exists()).toBe(false)
  })

  it('says so when nothing qualifies, and reports a failed bulk action', async () => {
    const empty = setup(Promise.resolve(preview([])))
    await flushPromises()
    expect(empty.wrapper.find('[data-empty]').exists()).toBe(true)
    expect(empty.wrapper.find('[data-action="bulk-confirm"]').attributes('disabled')).toBeDefined()
    vi.unstubAllGlobals()

    const bulk = vi.fn().mockRejectedValue({ data: { detail: 'minScore must be at least 10.' } })
    const failing = setup(Promise.resolve(preview([candidate()])), bulk)
    await flushPromises()
    await failing.wrapper.find('[data-action="bulk-confirm"]').trigger('click')
    await failing.wrapper.find('[data-action="bulk-confirm"]').trigger('click')
    await flushPromises()
    expect(failing.toast.failure).toHaveBeenCalledWith('Conflicts not resolved', 'minScore must be at least 10.')
    expect(failing.wrapper.emitted('resolved')).toBeUndefined()
  })

  it('shows a preview that failed to load', async () => {
    const { wrapper } = setup(Promise.reject(new Error('boom')))
    await flushPromises()
    expect(wrapper.find('aside').exists()).toBe(true)
    expect(wrapper.findAll('[data-candidate]')).toHaveLength(0)
  })
})
