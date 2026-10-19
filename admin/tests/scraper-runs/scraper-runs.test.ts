import { describe, expect, it } from 'vitest'
import { formatEta, formatRunArg, isScraperProcess, parseScraperSummary, progressPercent } from '~~/shared/utils/scraper-runs'

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
    expect(parsed.progress).toEqual({ done: 1200, total: 11000, label: 'profiles looked up', etaSeconds: null })
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
    expect(parseScraperSummary(null)).toEqual({ steps: [], currentStep: null, progress: null, run: null, results: [], errors: [], report: {} })
    expect(parseScraperSummary([1, 2]).steps).toEqual([])
  })

  it('ignores a progress gauge without a usable count', () => {
    expect(parseScraperSummary({ progress: { done: -1, total: 10 } }).progress).toBeNull()
    expect(parseScraperSummary({ progress: { done: 5, total: 0 } }).progress).toEqual({ done: 5, total: null, label: null, etaSeconds: null })
  })
})

describe('progressPercent', () => {
  it('rounds and caps at 100, null without a total', () => {
    expect(progressPercent({ done: 1, total: 3, label: null, etaSeconds: null })).toBe(33)
    expect(progressPercent({ done: 12, total: 10, label: null, etaSeconds: null })).toBe(100)
    expect(progressPercent({ done: 12, total: null, label: null, etaSeconds: null })).toBeNull()
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

describe('parseScraperSummary — run, results and errors (#2125)', () => {
  const parsed = parseScraperSummary({
    run: { kind: 'sweep', version: '1.3.0', args: { sources: ['deeplol'], discovery: true } },
    progress: { done: 10, total: 100, etaSeconds: 5400 },
    steps: [{ name: 'deeplol', label: 'DeepLoL', status: 'running' }],
    results: [
      { label: 'Personas intake', items: [{ label: 'Created', value: 6, tone: 'success' }, { label: 'Batches', value: '50 / 50' }, { label: 'Bad', value: { x: 1 } }, { label: 'Odd', value: 1, tone: 'purple' }] },
      { label: 'Empty', items: [] },
      { items: [{ label: 'x', value: 1 }] },
    ],
    errors: { trackingthepros: { count: 1, last: ['timeout'] }, deeplol: { count: 4, last: ['502', 7], blocked: 'HTTP 429 x5' }, junk: 'x' },
    personasByPrimarySource: { leaguepedia: 1 },
  })

  it('reads the run, the ETA and keeps the rest as the report', () => {
    expect(parsed.run).toEqual({ kind: 'sweep', version: '1.3.0', args: { sources: ['deeplol'], discovery: true } })
    expect(parsed.progress?.etaSeconds).toBe(5400)
    expect(parsed.report).toEqual({ personasByPrimarySource: { leaguepedia: 1 } })
  })

  it('keeps well-formed result groups and items only', () => {
    expect(parsed.results).toEqual([{ label: 'Personas intake', items: [
      { label: 'Created', value: 6, tone: 'success' },
      { label: 'Batches', value: '50 / 50', tone: null },
      { label: 'Odd', value: 1, tone: null },
    ] }])
  })

  it('sorts errors by count and names a source after its step', () => {
    expect(parsed.errors).toEqual([
      { source: 'deeplol', label: 'DeepLoL', count: 4, last: ['502'], blocked: 'HTTP 429 x5' },
      { source: 'trackingthepros', label: null, count: 1, last: ['timeout'], blocked: null },
    ])
  })

  it('reads a run without args, and no run at all', () => {
    expect(parseScraperSummary({ run: { kind: 'weekly' } }).run).toEqual({ kind: 'weekly', version: null, args: {} })
    expect(parseScraperSummary({ run: 'x', results: 'x', errors: [] }).run).toBeNull()
  })
})

describe('formatRunArg / formatEta', () => {
  it('prints arguments readably', () => {
    expect(formatRunArg(['leaguepedia', 'deeplol'])).toBe('leaguepedia, deeplol')
    expect(formatRunArg(true)).toBe('on')
    expect(formatRunArg(false)).toBe('off')
    expect(formatRunArg(28)).toBe('28')
  })

  it('prints a coarse remaining time', () => {
    expect(formatEta(null)).toBeNull()
    expect(formatEta(undefined)).toBeNull()
    expect(formatEta(30)).toBe('< 1m')
    expect(formatEta(240)).toBe('~4m')
    expect(formatEta(7860)).toBe('~2h 11m')
  })
})
