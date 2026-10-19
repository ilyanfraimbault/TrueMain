import type { ProcessIteration, ProcessRun } from '~~/shared/types/ops'
import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import ProcessesIterationRow from '~~/app/components/processes/ProcessesIterationRow.vue'
import ProcessesIterationSteps from '~~/app/components/processes/ProcessesIterationSteps.vue'
import ProcessesRecentIterations from '~~/app/components/processes/ProcessesRecentIterations.vue'

// Pins what an operator reads off the recent-iterations list: the one-line row (kind,
// span, duration, icon tallies and their wording), the hover recap of the step under
// the pointer, and the step timeline an expanded row shows. Nuxt UI components are
// stubbed down to the props and events these components actually use.

const global = {
  stubs: {
    UBadge: { props: ['label', 'icon'], template: '<span class="badge" :data-icon="icon">{{ label }}</span>' },
    UTooltip: { props: ['text'], template: '<span class="tooltip" :data-text="text"><slot /></span>' },
    UIcon: { props: ['name'], template: '<i :data-icon="name" />' },
    UButton: { props: ['label'], emits: ['click'], template: '<button type="button" class="ubutton" @click="$emit(\'click\')">{{ label }}</button>' },
    UCollapsible: {
      props: ['open'],
      emits: ['update:open'],
      template: '<div class="collapsible" @click="$emit(\'update:open\', !open)"><slot /><div v-if="open" class="content" @click.stop><slot name="content" /></div></div>',
    },
    UTimeline: {
      props: ['items'],
      emits: ['select'],
      template: `<ol class="timeline"><li v-for="item in items" :key="item.value" @click="$emit('select', $event, item)">
        <slot name="title" :item="item">{{ item.title }}</slot><p class="desc">{{ item.description }}</p></li></ol>`,
    },
    UTabs: {
      props: ['modelValue', 'items'],
      emits: ['update:modelValue'],
      template: '<div class="tabs"><button v-for="tab in items" :key="tab.value" type="button" class="tab" @click="$emit(\'update:modelValue\', tab.value)">{{ tab.label }}</button></div>',
    },
    UPagination: { template: '<nav class="pagination" />' },
    USkeleton: { template: '<div class="skeleton" />' },
    PanelTitle: { props: ['title'], template: '<div class="panel-title">{{ title }}</div>' },
  },
}

function run(processName: string, startedAtUtc: string, durationMs: number, overrides: Partial<ProcessRun> = {}): ProcessRun {
  return {
    id: `${processName}-${startedAtUtc}`,
    processName,
    startedAtUtc,
    finishedAtUtc: new Date(Date.parse(startedAtUtc) + durationMs).toISOString(),
    durationMs,
    status: 'Success',
    error: null,
    host: 'test',
    jobMode: 'FetchLane',
    lastHeartbeatAtUtc: null,
    summary: null,
    ...overrides,
  }
}

// A fetch pass shaped like the preprod ones: two cadence skips, a 30 s harvest, a
// 90 s ingestion that recorded counters, and a refresh that failed.
const fetchPass: ProcessIteration = {
  iterationId: 'fetch-1',
  jobMode: 'FetchLane',
  startedAtUtc: '2026-10-18T21:10:00.000Z',
  lastActivityAtUtc: '2026-10-18T21:12:00.000Z',
  isRunning: false,
  runs: [
    run('LadderSync', '2026-10-18T21:10:00.000Z', 3, { status: 'Skipped', summary: { reason: 'Within MinRunInterval; ladder sync skipped this iteration.', skipped: true } }),
    run('Discovery', '2026-10-18T21:10:00.000Z', 2, { status: 'Skipped' }),
    run('Harvest', '2026-10-18T21:10:00.000Z', 30_000, { summary: { candidatesInserted: 21, candidatesUpdated: 259 } }),
    run('MatchIngestion', '2026-10-18T21:10:30.000Z', 90_000, { summary: { accountsProcessed: 25, matchesInserted: 310 } }),
    run('AccountRefresh', '2026-10-18T21:12:00.000Z', 1, { status: 'Failed', error: 'Npgsql timeout\n   at Stack.Trace()' }),
  ],
}

function mountRow(iteration: ProcessIteration = fetchPass, open = false) {
  return mount(ProcessesIterationRow, { props: { iteration, open }, global })
}

describe('ProcessesIterationRow', () => {
  it('reads as one line: kind, duration and an icon per outcome, worded in a tooltip', () => {
    const wrapper = mountRow()

    expect(wrapper.find('.badge').text()).toBe('Fetch')
    expect(wrapper.find('.badge').attributes('data-icon')).toBe('i-lucide-globe')
    const tooltips = wrapper.findAll('.tooltip').map(t => t.attributes('data-text'))
    expect(tooltips).toEqual([
      'The pass took 2m',
      '2 steps succeeded',
      '2 steps skipped, not due yet',
      '1 step failed',
    ])
    // A pass with a failed step is tinted, so it is found without reading the counts.
    expect(wrapper.find('.collapsible').classes()).toContain('bg-error/5')
  })

  it('draws only the steps that took time, sized by their duration', () => {
    const segments = mountRow().findAll('span.basis-0')

    expect(segments.map(s => s.attributes('style'))).toEqual([
      'flex-grow: 30000;',
      'flex-grow: 90000;',
      'flex-grow: 1;',
    ])
  })

  it('recaps the step under the pointer, and drops the recap when it leaves', async () => {
    const wrapper = mountRow()
    const bar = wrapper.find('span.flex.h-2')
    bar.element.getBoundingClientRect = () => ({ left: 0, width: 100 }) as DOMRect

    await bar.trigger('pointermove', { clientX: 60 })
    const card = wrapper.find('.pointer-events-none')
    expect(card.text()).toContain('Match Ingestion')
    expect(card.text()).toContain('75% of the pass')
    expect(card.text()).toContain('Matches Inserted')
    expect(card.text()).toContain('310')

    await bar.trigger('pointerleave')
    expect(wrapper.find('.pointer-events-none').exists()).toBe(false)
  })

  it('shows a failed step\'s first error line instead of its counters', async () => {
    const wrapper = mountRow()
    const bar = wrapper.find('span.flex.h-2')
    bar.element.getBoundingClientRect = () => ({ left: 0, width: 100 }) as DOMRect

    await bar.trigger('pointermove', { clientX: 100 })
    const card = wrapper.find('.pointer-events-none')
    expect(card.text()).toContain('Npgsql timeout')
    expect(card.text()).not.toContain('Stack.Trace')
  })

  it('toggles open on click and offers the run summaries once open', async () => {
    const closed = mountRow()
    await closed.find('button').trigger('click')
    expect(closed.emitted('update:open')).toEqual([[true]])

    const open = mountRow(fetchPass, true)
    expect(open.find('.timeline').exists()).toBe(true)
    await open.find('.ubutton').trigger('click')
    expect(open.emitted('openIteration')).toEqual([[fetchPass]])
  })
})

describe('ProcessesIterationSteps', () => {
  it('lists every step of the lane with its time, duration and recap', () => {
    const wrapper = mount(ProcessesIterationSteps, { props: { iteration: fetchPass }, global })
    const items = wrapper.findAll('.timeline li')
    const text = (title: string) => items.find(item => item.text().includes(title))!.text()

    expect(items).toHaveLength(9)
    expect(text('Ladder Sync')).toContain('Skipped')
    expect(text('Ladder Sync')).toContain('Within MinRunInterval; ladder sync skipped this iteration.')
    expect(text('Harvest')).toContain('30.0s')
    expect(text('Harvest')).toContain('Candidates Inserted 21 · Candidates Updated 259')
    expect(text('Account Refresh')).toContain('Npgsql timeout')
    expect(text('Pace Sampling')).toContain('Never started in this pass.')
  })

  it('opens the run of a step that ran, and nothing for one that never started', async () => {
    const wrapper = mount(ProcessesIterationSteps, { props: { iteration: fetchPass }, global })
    const items = wrapper.findAll('.timeline li')

    await items.find(item => item.text().includes('Harvest'))!.trigger('click')
    await items.find(item => item.text().includes('Pace Sampling'))!.trigger('click')

    expect(wrapper.emitted('openRun')).toHaveLength(1)
    expect((wrapper.emitted('openRun')![0]![0] as ProcessRun).processName).toBe('Harvest')
  })

  it('splits a Full pass into one timeline per lane', () => {
    const full: ProcessIteration = {
      ...fetchPass,
      jobMode: 'Full',
      runs: [
        run('Harvest', '2026-10-18T21:10:00.000Z', 1000, { jobMode: 'Full' }),
        run('MainAnalysis', '2026-10-18T21:11:00.000Z', 1000, { jobMode: 'Full' }),
      ],
    }
    const wrapper = mount(ProcessesIterationSteps, { props: { iteration: full }, global })

    expect(wrapper.findAll('.timeline')).toHaveLength(2)
    expect(wrapper.text()).toContain('Fetch lane')
    expect(wrapper.text()).toContain('Aggregate lane')
  })
})

describe('ProcessesRecentIterations', () => {
  const base = { iterations: [fetchPass], pending: false, error: null, total: 1, pageSize: 8, serverPage: 1, page: 1, lane: 'all' as const }

  it('says why the list is empty, per lane', () => {
    const all = mount(ProcessesRecentIterations, { props: { ...base, iterations: [] }, global })
    expect(all.text()).toContain('No finished iterations yet.')

    const fetch = mount(ProcessesRecentIterations, { props: { ...base, iterations: [], lane: 'fetch' }, global })
    expect(fetch.text()).toContain('No finished iterations of this lane yet.')
  })

  it('shows skeletons while loading and a message on error', () => {
    expect(mount(ProcessesRecentIterations, { props: { ...base, pending: true }, global }).findAll('.skeleton')).toHaveLength(4)
    expect(mount(ProcessesRecentIterations, { props: { ...base, error: new Error('x') }, global }).text())
      .toContain('Failed to load recent iterations.')
  })

  it('switches lane from the tabs and pages only when there is more than a page', async () => {
    const wrapper = mount(ProcessesRecentIterations, { props: base, global })
    expect(wrapper.find('.pagination').exists()).toBe(false)

    await wrapper.findAll('.tab').find(tab => tab.text() === 'Aggregate')!.trigger('click')
    expect(wrapper.emitted('update:lane')).toEqual([['aggregate']])

    const paged = mount(ProcessesRecentIterations, { props: { ...base, total: 20 }, global })
    expect(paged.find('.pagination').exists()).toBe(true)
    expect(paged.text()).toContain('Page 1 of 3')
  })

  it('expands a row, and collapses everything when the page changes', async () => {
    const wrapper = mount(ProcessesRecentIterations, { props: base, global })

    await wrapper.find('.collapsible button').trigger('click')
    expect(wrapper.find('.timeline').exists()).toBe(true)

    await wrapper.find('.timeline li').trigger('click')
    expect(wrapper.emitted('openRun')).toHaveLength(1)

    await wrapper.setProps({ page: 2 })
    expect(wrapper.find('.timeline').exists()).toBe(false)
  })
})
