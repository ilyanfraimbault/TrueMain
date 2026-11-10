import type { ProcessRunReadModel } from '#site-shared/contract/api'
import { describe, expect, it } from 'vitest'
import { buildLaneBranches, laneForProcess, pickCurrentLanes } from '~~/shared/utils/pipeline-lanes'
import { CATALOG, CHAIN } from './catalog'

// The lanes come from the API's pipeline catalog (`GET /ops/pipeline`); the backend pins its
// partition and order. These pin how the admin draws a pass against it.

function run(processName: string, overrides: Partial<ProcessRunReadModel> = {}): ProcessRunReadModel {
  return {
    id: processName,
    processName,
    startedAtUtc: '2026-09-02T18:00:00.000Z',
    finishedAtUtc: '2026-09-02T18:01:00.000Z',
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

const FETCH_STEPS = CATALOG.lanes.find(lane => lane.id === 'fetch')!.steps
const AGGREGATE_STEPS = CATALOG.lanes.find(lane => lane.id === 'aggregate')!.steps

describe('laneForProcess', () => {
  it('resolves a step to its lane, and an unknown name to none', () => {
    expect(laneForProcess(CATALOG, 'MatchIngestion')).toBe('fetch')
    expect(laneForProcess(CATALOG, 'ChampionBanAggregation')).toBe('aggregate')
    expect(laneForProcess(CATALOG, 'SomethingNewNobodyDeclared')).toBeNull()
  })
})

describe('buildLaneBranches', () => {
  it('renders only the lane that ran, with no phantom steps from the other', () => {
    const branches = buildLaneBranches(CATALOG, FETCH_STEPS.map(step => run(step)))

    expect(branches.map(branch => branch.id)).toEqual(['fetch'])
    expect(branches[0]!.links.map(link => link.processName)).toEqual([...FETCH_STEPS])
    expect(branches[0]!.links.every(link => link.run !== null)).toBe(true)
  })

  it('renders both branches for a Full pass', () => {
    const branches = buildLaneBranches(CATALOG, CHAIN.map(step => run(step)))

    expect(branches.map(branch => branch.id)).toEqual(['fetch', 'aggregate'])
    expect(branches.flatMap(branch => branch.links)).toHaveLength(CHAIN.length)
  })

  it('marks the steps of a partially-run lane as notRun without dropping them', () => {
    const branches = buildLaneBranches(CATALOG, [run('MatchTeamPositionCorrection'), run('MainAnalysis')])

    expect(branches).toHaveLength(1)
    const [branch] = branches
    expect(branch!.links).toHaveLength(AGGREGATE_STEPS.length)
    expect(branch!.links.filter(link => link.outcome === 'notRun')).toHaveLength(
      AGGREGATE_STEPS.length - 2,
    )
  })

  it('shows the bare pipeline shape when nothing has run', () => {
    const branches = buildLaneBranches(CATALOG, [])

    expect(branches.map(branch => branch.id)).toEqual(['fetch', 'aggregate'])
    expect(branches.every(branch => !branch.ran)).toBe(true)
    expect(branches.flatMap(branch => branch.links).every(link => link.outcome === 'notRun')).toBe(true)
  })

  it('keeps a run whose process belongs to no lane, in a trailing branch', () => {
    const branches = buildLaneBranches(CATALOG, [run('MatchIngestion'), run('SomeUndeclaredStep')])

    expect(branches.map(branch => branch.id)).toEqual(['fetch', 'other'])
    expect(branches.at(-1)!.links.map(link => link.processName)).toEqual(['SomeUndeclaredStep'])
  })

  it('draws every run under its raw name in the trailing branch while the catalog is not known', () => {
    const branches = buildLaneBranches(null, [
      run('LadderSync', { jobMode: 'Full' }),
      run('MainAnalysis', { jobMode: 'Full' }),
    ])

    expect(branches.map(branch => branch.id)).toEqual(['other'])
    expect(branches[0]!.links.map(link => link.processName)).toEqual(['LadderSync', 'MainAnalysis'])
    expect(laneForProcess(null, 'LadderSync')).toBeNull()
  })

  it('takes the most alarming status as the lane outcome', () => {
    const failed = buildLaneBranches(CATALOG, [
      run('LadderSync'),
      run('Discovery', { status: 'Failed' }),
      run('MatchIngestion', { status: 'Skipped' }),
    ])
    expect(failed[0]!.outcome).toBe('Failed')

    const running = buildLaneBranches(CATALOG, [
      run('LadderSync', { status: 'Failed' }),
      run('Discovery', { status: 'Running', finishedAtUtc: null }),
    ])
    expect(running[0]!.outcome).toBe('Running')

    // A lane a shutdown cut short is incomplete however many steps finished first, so
    // Cancelled outranks Success — reading it as a success would hide the pass every
    // redeploy interrupts. It still yields to Abandoned, which claims the host died.
    const cancelled = buildLaneBranches(CATALOG, [
      run('LadderSync'),
      run('Discovery', { status: 'Cancelled' }),
    ])
    expect(cancelled[0]!.outcome).toBe('Cancelled')

    const abandoned = buildLaneBranches(CATALOG, [
      run('LadderSync', { status: 'Cancelled' }),
      run('Discovery', { status: 'Abandoned' }),
    ])
    expect(abandoned[0]!.outcome).toBe('Abandoned')
  })

  it('measures the lane from its first start to its last finish, and stays silent while in flight', () => {
    const finished = buildLaneBranches(CATALOG, [
      run('LadderSync', { startedAtUtc: '2026-09-02T18:00:00.000Z', finishedAtUtc: '2026-09-02T18:01:00.000Z' }),
      // A gap between the two runs is part of the lane's wall-clock span: the header
      // answers "how long did the lane take", not "how long was it busy".
      run('MatchIngestion', { startedAtUtc: '2026-09-02T18:05:00.000Z', finishedAtUtc: '2026-09-02T18:07:30.000Z' }),
    ])
    expect(finished[0]!.startedAtUtc).toBe('2026-09-02T18:00:00.000Z')
    expect(finished[0]!.durationMs).toBe(450_000)

    const inFlight = buildLaneBranches(CATALOG, [
      run('LadderSync'),
      run('MatchIngestion', { status: 'Running', finishedAtUtc: null }),
    ])
    expect(inFlight[0]!.durationMs).toBeNull()

    // The API mirrors a running run's start into `finishedAtUtc` so the iteration's
    // last-activity stamp advances; the status, not the timestamps, says in-flight.
    const mirrored = buildLaneBranches(CATALOG, [
      run('LadderSync', {
        status: 'Running',
        startedAtUtc: '2026-09-02T18:00:00.000Z',
        finishedAtUtc: '2026-09-02T18:00:00.000Z',
        durationMs: 0,
      }),
    ])
    expect(mirrored[0]!.durationMs).toBeNull()
  })

  it('shows the latest run of a process that ran twice in the pass', () => {
    const branches = buildLaneBranches(CATALOG, [
      run('MatchIngestion', { id: 'first', status: 'Failed' }),
      run('MatchIngestion', { id: 'retry', status: 'Success' }),
    ])
    const link = branches[0]!.links.find(candidate => candidate.processName === 'MatchIngestion')

    expect(link!.run!.id).toBe('retry')
    expect(link!.outcome).toBe('Success')
  })
})

describe('pickCurrentLanes', () => {
  it('shows each lane at its own newest iteration', () => {
    // Newest first, the way the API returns them: the fetch lane has run twice
    // since the aggregate lane's last pass.
    const lanes = pickCurrentLanes(CATALOG, [
      { runs: [run('LadderSync', { status: 'Running', finishedAtUtc: null })] },
      { runs: [run('LadderSync'), run('MatchIngestion')] },
      { runs: [run('MainAnalysis'), run('ChampionBanAggregation')] },
    ])

    expect(lanes.map(lane => lane.branch.id)).toEqual(['fetch', 'aggregate'])
    expect(lanes[0]!.isRunning).toBe(true)
    expect(lanes[0]!.branch.links.find(link => link.processName === 'MatchIngestion')!.outcome).toBe('notRun')
    expect(lanes[1]!.isRunning).toBe(false)
    expect(lanes[1]!.branch.ran).toBe(true)
  })

  it('keeps a lane that has not run in the window, marked not-run', () => {
    // The case the panel exists for: one lane stalled or dead. It must stay on
    // screen rather than disappear because no recent iteration mentions it.
    const lanes = pickCurrentLanes(CATALOG, [{ runs: [run('LadderSync'), run('MatchIngestion')] }])

    expect(lanes.map(lane => lane.branch.id)).toEqual(['fetch', 'aggregate'])
    const aggregate = lanes[1]!
    expect(aggregate.branch.ran).toBe(false)
    expect(aggregate.branch.outcome).toBe('notRun')
    expect(aggregate.isRunning).toBe(false)
  })

  it('shows every lane not-run when nothing has been recorded at all', () => {
    const lanes = pickCurrentLanes(CATALOG, [])

    expect(lanes.map(lane => lane.branch.id)).toEqual(['fetch', 'aggregate'])
    expect(lanes.every(lane => !lane.branch.ran)).toBe(true)
  })

  it('reports a Full pass as one running lane, not two', () => {
    // The iteration is running, but only the branch holding the running step is.
    const lanes = pickCurrentLanes(CATALOG, [{
      runs: [
        run('LadderSync'),
        run('MainAnalysis', { status: 'Running', finishedAtUtc: null }),
      ],
    }])

    expect(lanes.map(lane => lane.isRunning)).toEqual([false, true])
  })
})

describe('a deliberate single-process run', () => {
  // The ingestor records the mode it opened the pass with, because a one-off
  // `RetentionOnly` and an aggregate lane that has only reached its first step are
  // indistinguishable by their contents. Drawing the one-off against a whole lane
  // would put eleven "Not run" chips beside it and claim they were skipped — the
  // misreading the lane split exists to remove, just narrower.
  it('is drawn as only the step that ran', () => {
    const branches = buildLaneBranches(CATALOG, [
      run('MatchDataRetention', { jobMode: 'MatchDataRetentionOnly' }),
    ])

    expect(branches).toHaveLength(1)
    expect(branches[0]!.links.map(link => link.processName)).toEqual(['MatchDataRetention'])
    expect(branches[0]!.label).toBe('MatchDataRetentionOnly')
  })

  it('does not narrow a composite mode', () => {
    // FetchLane is a sequence, so a pass that has only reached LadderSync really has
    // seven steps still to come and must keep showing them.
    const branches = buildLaneBranches(CATALOG, [run('LadderSync', { jobMode: 'FetchLane' })])

    expect(branches).toHaveLength(1)
    expect(branches[0]!.links.length).toBeGreaterThan(1)
  })

  it('falls back to the lane rendering when no mode was recorded', () => {
    // Runs written before the ingestor stamped the mode: guessing "one-off" from a
    // single run would hide the rest of a lane that had merely just started.
    const branches = buildLaneBranches(CATALOG, [run('MatchDataRetention')])

    expect(branches[0]!.links.length).toBeGreaterThan(1)
  })
})

// FetchWorkers (#1457) records two kinds of pass under one mode: the fetch lane's
// maintenance pass, which no longer sequences match fetching, and every batch of the
// permanent match-fetch workers running beside it.
describe('a FetchWorkers host', () => {
  it('draws a worker batch as its own branch, not as the fetch lane', () => {
    const branches = buildLaneBranches(CATALOG, [run('MatchIngestion', { jobMode: 'FetchWorkers' })])

    expect(branches).toHaveLength(1)
    expect(branches[0]!.id).toBe('workers')
    expect(branches[0]!.links.map(link => link.processName)).toEqual(['MatchIngestion'])
  })

  it('draws its maintenance pass as the fetch lane without match fetching', () => {
    const branches = buildLaneBranches(CATALOG, [run('LadderSync', { jobMode: 'FetchWorkers' })])

    expect(branches).toHaveLength(1)
    expect(branches[0]!.id).toBe('fetch')
    expect(branches[0]!.links.map(link => link.processName))
      .toEqual(FETCH_STEPS.filter(step => step !== 'MatchIngestion'))
  })

  it('keeps the maintenance pass in the live tree however many batches ran since', () => {
    const lanes = pickCurrentLanes(CATALOG, [
      { runs: [run('MatchIngestion', { jobMode: 'FetchWorkers', id: 'batch-2' })] },
      { runs: [run('MatchIngestion', { jobMode: 'FetchWorkers', id: 'batch-1' })] },
      { runs: [run('Scoring', { jobMode: 'FetchWorkers' })] },
    ])

    expect(lanes.map(lane => lane.branch.id)).toEqual(['fetch', 'aggregate', 'workers'])
    expect(lanes.find(lane => lane.branch.id === 'workers')!.branch.links[0]!.run!.id).toBe('batch-2')
    expect(lanes.find(lane => lane.branch.id === 'fetch')!.branch.ran).toBe(true)
  })
})
