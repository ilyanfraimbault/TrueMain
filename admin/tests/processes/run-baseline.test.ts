import type { ProcessRunBaseline } from '~~/shared/types/ops'
import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import ProcessSummaryView from '~~/app/components/ProcessSummaryView.vue'
import ProcessesBaselineBadge from '~~/app/components/processes/ProcessesBaselineBadge.vue'
import { flaggedCounters, flaggedMetric, formatDeviation } from '~~/shared/utils/process-baseline'

// Pins how a run's gap from its recent median reads (#2298): the wording of the gap,
// that only an API-flagged metric draws a badge, and that the summary view puts it
// next to the top-level counter it belongs to.

const global = {
  stubs: {
    UBadge: { props: ['label'], template: '<span class="badge">{{ label }}</span>' },
    UTooltip: { props: ['text'], template: '<span class="tooltip" :data-text="text"><slot /></span>' },
  },
}

const baseline: ProcessRunBaseline = {
  sampleCount: 20,
  duration: { median: 60_000, deviation: 1.8, flagged: true },
  counters: {
    inserted: { median: 120, deviation: -0.75, flagged: true },
    skipped: { median: 4, deviation: 0.25, flagged: false },
  },
}

describe('process baseline wording', () => {
  it('signs and rounds the gap to the percent', () => {
    expect(formatDeviation(1.8)).toBe('+180%')
    expect(formatDeviation(-0.604)).toBe('−60%')
    expect(formatDeviation(0)).toBe('0%')
    expect(formatDeviation(12.5)).toBe('+1,250%')
    expect(formatDeviation(null)).toBe('—')
  })

  it('keeps only the flagged metrics and counters', () => {
    expect(flaggedMetric(baseline.counters.skipped)).toBeNull()
    expect(flaggedMetric(undefined)).toBeNull()
    expect(flaggedMetric(baseline.duration)).toEqual(baseline.duration)
    expect(flaggedCounters(baseline)).toEqual(['inserted'])
    expect(flaggedCounters(null)).toEqual([])
  })
})

describe('ProcessesBaselineBadge', () => {
  it('draws a flagged duration with its median in the tooltip', () => {
    const wrapper = mount(ProcessesBaselineBadge, {
      props: { metric: baseline.duration, sampleCount: 20, kind: 'duration' },
      global,
    })
    expect(wrapper.find('.badge').text()).toBe('+180% vs median')
    expect(wrapper.find('.tooltip').attributes('data-text'))
      .toBe('Median of the last 20 successful runs: 1m')
  })

  it('formats a counter median as a number', () => {
    const wrapper = mount(ProcessesBaselineBadge, {
      props: { metric: baseline.counters.inserted, sampleCount: 20, kind: 'count' },
      global,
    })
    expect(wrapper.find('.tooltip').attributes('data-text'))
      .toBe('Median of the last 20 successful runs: 120')
  })

  it('draws nothing for a metric inside the thresholds', () => {
    const wrapper = mount(ProcessesBaselineBadge, {
      props: { metric: baseline.counters.skipped, sampleCount: 20, kind: 'count' },
      global,
    })
    expect(wrapper.find('.badge').exists()).toBe(false)
  })
})

describe('ProcessSummaryView — baseline', () => {
  it('badges the flagged top-level counter only', () => {
    const wrapper = mount(ProcessSummaryView, {
      props: { value: { inserted: 30, skipped: 5, nested: { inserted: 1 } }, baseline },
      global,
    })
    expect(wrapper.findAll('.badge').map(badge => badge.text())).toEqual(['−75% vs median'])
  })

  it('draws no badge without a baseline', () => {
    const wrapper = mount(ProcessSummaryView, { props: { value: { inserted: 30 } }, global })
    expect(wrapper.find('.badge').exists()).toBe(false)
  })
})
