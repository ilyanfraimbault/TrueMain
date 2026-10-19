import { describe, expect, it } from 'vitest'
import { isScraperProcess, parseScraperSummary, progressPercent } from '~~/shared/utils/scraper-runs'

describe('parseScraperSummary', () => {
  it('splits steps, current step and progress from the report', () => {
    const parsed = parseScraperSummary({
      currentStep: 'deeplol',
      progress: { done: 1200, total: 11000, label: 'profiles looked up' },
      steps: [
        { name: 'leaguepedia', status: 'done', startedAtUtc: '2026-10-18T22:15:06Z', finishedAtUtc: '2026-10-18T22:30:27Z', counters: { recordsKept: 4998, requests: 420, nested: { x: 1 } } },
        { name: 'deeplol', label: 'DeepLoL', status: 'running', startedAtUtc: '2026-10-18T23:44:16Z' },
      ],
      personas: 5147,
    })
    expect(parsed.currentStep).toBe('deeplol')
    expect(parsed.progress).toEqual({ done: 1200, total: 11000, label: 'profiles looked up' })
    expect(parsed.steps.map(s => [s.name, s.status])).toEqual([['leaguepedia', 'done'], ['deeplol', 'running']])
    expect(parsed.steps[0]!.counters).toEqual({ recordsKept: 4998, requests: 420 })
    expect(parsed.steps[1]!.finishedAtUtc).toBeNull()
    expect(parsed.steps.map(s => s.label)).toEqual([null, 'DeepLoL'])
    expect(parsed.report).toEqual({ personas: 5147 })
  })

  it('keeps a free-form summary whole and finds no steps', () => {
    const parsed = parseScraperSummary({ matchesIngested: 3, steps: 'nope' })
    expect(parsed.steps).toEqual([])
    expect(parsed.currentStep).toBeNull()
    expect(parsed.report).toEqual({ matchesIngested: 3 })
  })

  it('drops malformed steps and defaults an unknown status to done', () => {
    const parsed = parseScraperSummary({ steps: [null, { status: 'running' }, { name: 'ttp', status: 'weird' }] })
    expect(parsed.steps.map(s => [s.name, s.status])).toEqual([['ttp', 'done']])
  })

  it('reads no summary as empty', () => {
    expect(parseScraperSummary(null)).toEqual({ steps: [], currentStep: null, progress: null, report: {} })
    expect(parseScraperSummary([1, 2]).steps).toEqual([])
  })

  it('ignores a progress gauge without a usable count', () => {
    expect(parseScraperSummary({ progress: { done: -1, total: 10 } }).progress).toBeNull()
    expect(parseScraperSummary({ progress: { done: 5, total: 0 } }).progress).toEqual({ done: 5, total: null, label: null })
  })
})

describe('progressPercent', () => {
  it('rounds and caps at 100, null without a total', () => {
    expect(progressPercent({ done: 1, total: 3, label: null })).toBe(33)
    expect(progressPercent({ done: 12, total: 10, label: null })).toBe(100)
    expect(progressPercent({ done: 12, total: null, label: null })).toBeNull()
    expect(progressPercent(null)).toBeNull()
  })
})

describe('isScraperProcess', () => {
  it('knows the reporting ops scripts only', () => {
    expect(isScraperProcess('PersonaScraper')).toBe(true)
    expect(isScraperProcess('OtpSeeder')).toBe(true)
    expect(isScraperProcess('MatchIngestion')).toBe(false)
  })
})
