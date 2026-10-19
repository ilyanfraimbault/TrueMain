import type { ProcessRun, ProcessRunsResponse } from '~~/shared/types/ops'
import type { ScraperStep } from '~~/shared/utils/scraper-runs'
import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import { defineComponent, h } from 'vue'
import ProcessesScraperCard from '~~/app/components/processes/ProcessesScraperCard.vue'
import ProcessesScraperReport from '~~/app/components/processes/ProcessesScraperReport.vue'
import ProcessesScraperSteps from '~~/app/components/processes/ProcessesScraperSteps.vue'
import { parseScraperSummary, SCRAPER_PROCESSES } from '~~/shared/utils/scraper-runs'

// Nuxt UI and the app's own components are auto-imported in the app; here they are
// stand-ins that render the props a test reads.
const stub = (tag: string, text?: (props: Record<string, unknown>) => string) => defineComponent({
  props: ['label', 'title', 'subtitle', 'name', 'modelValue', 'error'],
  emits: ['click'],
  setup(props, { slots, emit }) {
    return () => h(tag, { 'data-stub': tag, 'onClick': () => emit('click') }, [
      text ? text(props) : '',
      slots.header?.(),
      slots.default?.(),
    ])
  },
})

const global = {
  components: {
    ProcessesScraperSteps,
    ProcessesScraperReport,
    UIcon: stub('i', p => String(p.name ?? '')),
    UBadge: stub('mark', p => String(p.label ?? '')),
    UProgress: stub('progress', p => String(p.modelValue ?? '')),
    UCard: stub('section'),
    UButton: stub('button', p => String(p.label ?? '')),
    PanelTitle: stub('h3', p => `${p.title} — ${p.subtitle}`),
    FetchErrorAlert: stub('aside', p => String(p.title ?? 'error')),
  },
}

const step = (over: Partial<ScraperStep>): ScraperStep => ({
  name: 'leaguepedia', label: null, status: 'done', startedAtUtc: null, finishedAtUtc: null, counters: {}, ...over,
})

describe('ProcessesScraperSteps', () => {
  it('lists each step with its label, duration and counters', () => {
    const wrapper = mount(ProcessesScraperSteps, {
      global,
      props: {
        steps: [
          step({ startedAtUtc: '2026-10-18T22:15:00Z', finishedAtUtc: '2026-10-18T22:30:00Z', counters: { recordsKept: 4998, mode: 'wikitext' } }),
          step({ name: 'deeplol', label: 'DeepLoL', status: 'running', startedAtUtc: '2026-10-18T23:44:00Z' }),
          step({ name: 'merge', status: 'skipped' }),
        ],
        progress: { done: 250, total: 1000, label: 'profiles looked up', etaSeconds: 600 },
        live: true,
      },
    })
    const items = wrapper.findAll('li')
    expect(items).toHaveLength(3)
    expect(items[0]!.text()).toContain('Leaguepedia')
    expect(items[0]!.text()).toContain('15m')
    expect(items[0]!.text()).toContain('Records Kept: 4,998')
    expect(items[0]!.text()).toContain('Mode: wikitext')
    expect(items[1]!.text()).toContain('DeepLoL')
    expect(items[1]!.find('[data-stub="progress"]').text()).toBe('25')
    expect(items[1]!.text()).toContain('250 / 1,000 (25 %)')
    expect(items[1]!.text()).toContain('profiles looked up')
    expect(items[1]!.text()).toContain('~10m left')
    expect(items[2]!.text()).toContain('Merge')
  })

  it('does not tick a running step of a dead run, and shows a gauge without total as a count', () => {
    const wrapper = mount(ProcessesScraperSteps, {
      global,
      props: {
        steps: [step({ name: 'seed', status: 'running', startedAtUtc: '2026-10-18T23:44:00Z' })],
        progress: { done: 12, total: null, label: null, etaSeconds: 600 },
        live: false,
      },
    })
    expect(wrapper.find('[data-stub="progress"]').exists()).toBe(false)
    expect(wrapper.find('li').text()).toContain('12')
    expect(wrapper.find('i').classes()).toContain('text-warning')
    expect(wrapper.text()).not.toContain('left')  // a dead run has no ETA
  })
})

function run(over: Partial<ProcessRun>): ProcessRun {
  return {
    id: 'r1',
    processName: 'PersonaScraper',
    startedAtUtc: '2026-10-18T22:15:00Z',
    finishedAtUtc: '2026-10-19T01:35:00Z',
    durationMs: 12_000_000,
    status: 'Success',
    error: null,
    host: 'ops-host',
    lastHeartbeatAtUtc: null,
    jobMode: null,
    summary: null,
    ...over,
  }
}

function response(runs: ProcessRun[]): ProcessRunsResponse {
  return { runs, rollup: [], total: runs.length, page: 1, pageSize: 9 }
}

describe('ProcessesScraperCard', () => {
  const scraper = SCRAPER_PROCESSES[0]!

  it('shows the latest run with its current step and steps, then the history', async () => {
    const latest = run({
      id: 'live',
      status: 'Running',
      startedAtUtc: new Date(Date.now() - 90 * 60_000).toISOString(),
      summary: { currentStep: 'deeplol', steps: [{ name: 'deeplol', label: 'DeepLoL', status: 'running' }], run: { kind: 'sweep', version: '1.3.0', args: { sources: ['deeplol'], ladder: 'KR' } }, errors: { deeplol: { count: 2, last: ['502'] } } },
    })
    const older = run({ id: 'old', status: 'Failed' })
    const wrapper = mount(ProcessesScraperCard, {
      global,
      props: { scraper, data: response([latest, older]), pending: false, error: null },
    })
    expect(wrapper.text()).toContain('Persona scraper')
    expect(wrapper.text()).toContain('Running for')
    expect(wrapper.text()).toContain('1h 30m')
    expect(wrapper.text()).toContain('deeplol')
    expect(wrapper.text()).toContain('ops-host')
    expect(wrapper.text()).toContain('sweep')
    expect(wrapper.text()).toContain('v1.3.0')
    expect(wrapper.text()).toContain('Sources deeplol · Ladder KR')
    expect(wrapper.text()).toContain('2 errors across 1 source')
    expect(wrapper.findAll('li').length).toBeGreaterThanOrEqual(2)

    await wrapper.find('ul button').trigger('click')
    expect(wrapper.emitted('openRun')?.[0]).toEqual([older])
    await wrapper.find('[data-stub="button"]').trigger('click')
    expect(wrapper.emitted('openRun')?.[1]).toEqual([latest])
  })

  it('shows a finished run with its duration and error', () => {
    const wrapper = mount(ProcessesScraperCard, {
      global,
      props: { scraper, data: response([run({ status: 'Failed', error: 'boom' })]), pending: false, error: null },
    })
    expect(wrapper.text()).toContain('Duration')
    expect(wrapper.text()).toContain('3h 20m')
    expect(wrapper.find('pre').text()).toBe('boom')
    expect(wrapper.text()).not.toContain('Previous runs')
  })

  it('says when nothing ran yet, and surfaces a fetch error', () => {
    const empty = mount(ProcessesScraperCard, { global, props: { scraper, data: response([]), pending: false, error: null } })
    expect(empty.text()).toContain('No run reported yet.')
    const loading = mount(ProcessesScraperCard, { global, props: { scraper, data: null, pending: true, error: null } })
    expect(loading.text()).toContain('Loading…')
    const failed = mount(ProcessesScraperCard, { global, props: { scraper, data: null, pending: false, error: new Error('x') } })
    expect(failed.find('[data-stub="aside"]').exists()).toBe(true)
  })
})

describe('ProcessesScraperReport', () => {
  const summary = parseScraperSummary({
    run: { kind: 'sweep', version: '1.3.0', args: { sources: ['leaguepedia', 'deeplol'], discovery: true, gist: false } },
    steps: [{ name: 'deeplol', label: 'DeepLoL', status: 'done' }],
    results: [{ label: 'Accounts', items: [{ label: 'Linked', value: 7290, tone: 'success' }, { label: 'Conflict', value: 5, tone: 'warning' }] }],
    errors: { deeplol: { count: 4, last: ['streamer/x: non-JSON HTTP 502'], blocked: 'HTTP 429 x5' }, trackingthepros: { count: 1, last: [] } },
  })

  it('renders results groups with their tones', () => {
    const wrapper = mount(ProcessesScraperReport, { global, props: { summary } })
    const values = wrapper.findAll('section dd')
    expect(values.map(v => v.text())).toEqual(['7,290', '5'])
    expect(values[0]!.classes()).toContain('text-success')
    expect(values[1]!.classes()).toContain('text-warning')
    expect(wrapper.text()).not.toContain('Invocation')
  })

  it('renders errors per source with their last messages and the drop reason', () => {
    const wrapper = mount(ProcessesScraperReport, { global, props: { summary } })
    expect(wrapper.text()).toContain('5 errors across 2 sources')
    expect(wrapper.text()).toContain('DeepLoL')
    expect(wrapper.text()).toContain('dropped for the run: HTTP 429 x5')
    expect(wrapper.findAll('li').map(li => li.text())).toEqual(['streamer/x: non-JSON HTTP 502'])
  })

  it('shows the invocation on request', () => {
    const wrapper = mount(ProcessesScraperReport, { global, props: { summary, showArgs: true } })
    expect(wrapper.text()).toContain('Invocation')
    expect(wrapper.findAll('dt').map(d => d.text())).toEqual(expect.arrayContaining(['Sources', 'Discovery', 'Gist']))
    expect(wrapper.text()).toContain('leaguepedia, deeplol')
  })

  it('renders nothing for a summary without them', () => {
    const wrapper = mount(ProcessesScraperReport, { global, props: { summary: parseScraperSummary({}) } })
    expect(wrapper.text()).toBe('')
  })
})
