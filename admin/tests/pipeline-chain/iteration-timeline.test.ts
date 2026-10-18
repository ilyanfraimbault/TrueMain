import type { ProcessIteration, ProcessRun } from '~~/shared/types/ops'
import { describe, expect, it } from 'vitest'
import {
  iterationKind,
  iterationSpan,
  parallelPasses,
  placeRun,
  statusCounts,
  timedRuns,
} from '~~/shared/utils/iteration-timeline'

// The compact iteration rows read everything off these helpers: the badge, the bar and
// the "ran alongside" line. The overlap rule is the one an operator acts on, so it is
// pinned with the shapes that should NOT pair as much as the ones that should.

function run(processName: string, overrides: Partial<ProcessRun> = {}): ProcessRun {
  return {
    id: `${processName}-${overrides.startedAtUtc ?? 'x'}`,
    processName,
    startedAtUtc: '2026-10-18T18:00:00.000Z',
    finishedAtUtc: '2026-10-18T18:01:00.000Z',
    durationMs: 60_000,
    status: 'Success',
    error: null,
    host: 'test',
    jobMode: null,
    lastHeartbeatAtUtc: null,
    summary: null,
    ...overrides,
  }
}

function iteration(
  id: string,
  jobMode: string | null,
  startedAtUtc: string,
  lastActivityAtUtc: string,
  runs: ProcessRun[] = [],
): ProcessIteration {
  return { iterationId: id, jobMode, startedAtUtc, lastActivityAtUtc, isRunning: false, runs }
}

describe('iterationKind', () => {
  it('names the lane modes and keeps a single-process mode verbatim', () => {
    expect(iterationKind(iteration('a', 'FetchLane', '', '')).id).toBe('fetch')
    expect(iterationKind(iteration('a', 'AggregateLane', '', '')).label).toBe('Aggregate')
    expect(iterationKind(iteration('a', 'Full', '', '')).id).toBe('full')
    expect(iterationKind(iteration('a', 'HarvestOnly', '', ''))).toEqual({ id: 'single', label: 'HarvestOnly' })
  })

  it('falls back to the lane of the processes for a pass recorded before the mode was stamped', () => {
    expect(iterationKind(iteration('a', null, '', '', [run('Discovery'), run('Scoring')])).id).toBe('fetch')
    expect(iterationKind(iteration('a', null, '', '', [run('Discovery'), run('MainAnalysis')])).id).toBe('full')
    expect(iterationKind(iteration('a', null, '', '', [run('SomethingNew')]))).toEqual({ id: 'single', label: 'SomethingNew' })
  })
})

describe('placeRun', () => {
  const span = iterationSpan(iteration('a', 'FetchLane', '2026-10-18T18:00:00.000Z', '2026-10-18T18:10:00.000Z'))

  it('places a run by its start and duration inside the pass', () => {
    const placement = placeRun(run('Harvest', { startedAtUtc: '2026-10-18T18:05:00.000Z', durationMs: 120_000 }), span)

    expect(placement.offsetPct).toBeCloseTo(50)
    expect(placement.widthPct).toBeCloseTo(20)
  })

  it('keeps a near-instant run visible and never past the end of the pass', () => {
    expect(placeRun(run('ManualSeed', { durationMs: 5 }), span).widthPct).toBeGreaterThan(0)

    const late = placeRun(run('PaceSampling', { startedAtUtc: '2026-10-18T18:09:30.000Z', durationMs: 120_000 }), span)
    expect(late.offsetPct + late.widthPct).toBeLessThanOrEqual(100)
  })
})

describe('timedRuns and statusCounts', () => {
  const runs = [
    run('Scoring', { startedAtUtc: '2026-10-18T18:03:00.000Z' }),
    run('Discovery', { status: 'Skipped', durationMs: 3 }),
    run('Harvest', { startedAtUtc: '2026-10-18T18:01:00.000Z' }),
    run('MatchIngestion', { status: 'Failed', startedAtUtc: '2026-10-18T18:04:00.000Z' }),
  ]

  it('draws only the runs that took time, in the order they ran', () => {
    expect(timedRuns(runs).map(r => r.processName)).toEqual(['Harvest', 'Scoring', 'MatchIngestion'])
  })

  it('counts every run, skipped ones included', () => {
    expect(statusCounts(runs)).toEqual({ Success: 2, Skipped: 1, Failed: 1 })
  })
})

describe('parallelPasses', () => {
  const fetch = iteration('fetch', 'FetchLane', '2026-10-18T19:42:00.000Z', '2026-10-18T20:17:00.000Z')
  const aggregate = iteration('agg', 'AggregateLane', '2026-10-18T19:51:00.000Z', '2026-10-18T21:04:00.000Z')
  const laterFetch = iteration('fetch2', 'FetchLane', '2026-10-18T21:10:00.000Z', '2026-10-18T21:30:00.000Z')
  const full = iteration('full', 'Full', '2026-10-18T19:00:00.000Z', '2026-10-18T22:00:00.000Z')
  const page = [laterFetch, aggregate, fetch, full]

  it('pairs a fetch pass with the aggregate passes it overlapped, both ways', () => {
    expect(parallelPasses(fetch, page).map(it => it.iterationId)).toEqual(['agg'])
    expect(parallelPasses(aggregate, page).map(it => it.iterationId)).toEqual(['fetch'])
  })

  it('never pairs a pass with one that only touched it end to start', () => {
    const touching = iteration('agg2', 'AggregateLane', '2026-10-18T21:30:00.000Z', '2026-10-18T21:40:00.000Z')
    expect(parallelPasses(laterFetch, [touching])).toEqual([])
  })

  it('pairs neither a Full pass nor a single-process one', () => {
    const single = iteration('one', 'HarvestOnly', '2026-10-18T19:45:00.000Z', '2026-10-18T19:46:00.000Z')

    expect(parallelPasses(full, page)).toEqual([])
    expect(parallelPasses(single, page)).toEqual([])
    expect(parallelPasses(fetch, [single, full])).toEqual([])
  })
})
