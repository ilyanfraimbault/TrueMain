import type { ProcessIteration, ProcessRun } from '~~/shared/types/ops'
import { describe, expect, it } from 'vitest'
import {
  iterationKind,
  statusCounts,
  timedRuns,
} from '~~/shared/utils/iteration-timeline'

// The compact iteration rows read their badge, bar and counts off these helpers.

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
