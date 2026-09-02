import type { ProcessRun } from '~~/shared/types/ops'
import { describe, expect, it } from 'vitest'
import { PIPELINE_CHAIN, PIPELINE_LANES } from '~~/shared/types/ops'
import { buildLaneBranches, laneForProcess, pickCurrentLanes } from '~~/shared/utils/pipeline-lanes'

// PIPELINE_LANES is a hand-maintained copy of the ingestor's FetchLanePipeline /
// AggregateLanePipeline, the way PIPELINE_CHAIN is one of FullPipeline — and the
// backend pins the same property on its side. It has to be pinned here too, because
// the failure modes are silent: a step in NEITHER lane stops being rendered at all
// (the flat chain at least drew it in the wrong place), and a step in BOTH is drawn
// twice, once per branch.

function run(processName: string, overrides: Partial<ProcessRun> = {}): ProcessRun {
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

const FETCH_STEPS = PIPELINE_LANES.find(lane => lane.id === 'fetch')!.steps
const AGGREGATE_STEPS = PIPELINE_LANES.find(lane => lane.id === 'aggregate')!.steps

describe('PIPELINE_LANES', () => {
  it('partitions PIPELINE_CHAIN exactly', () => {
    const laneSteps = PIPELINE_LANES.flatMap(lane => lane.steps)

    expect(new Set(laneSteps).size).toBe(laneSteps.length)
    expect([...laneSteps].sort()).toEqual([...PIPELINE_CHAIN].sort())
  })

  it('keeps each lane in PIPELINE_CHAIN order', () => {
    // Order *within* a lane is load-bearing on the backend (the ban fold needs elo
    // stamped, the timeline prune must not precede the powerspike fold), so a branch
    // that renders its steps in another order would misdescribe the run.
    for (const lane of PIPELINE_LANES) {
      const positions = lane.steps.map(step => PIPELINE_CHAIN.indexOf(step))

      expect(positions).toEqual([...positions].sort((a, b) => a - b))
    }
  })

  it('names and describes every lane', () => {
    for (const lane of PIPELINE_LANES) {
      expect(lane.label.trim()).not.toBe('')
      expect(lane.description.trim()).not.toBe('')
    }
  })

  it('resolves a step to its lane, and an unknown name to none', () => {
    expect(laneForProcess('MatchIngestion')).toBe('fetch')
    expect(laneForProcess('ChampionBanAggregation')).toBe('aggregate')
    expect(laneForProcess('SomethingNewNobodyDeclared')).toBeNull()
  })
})

describe('buildLaneBranches', () => {
  it('renders only the lane that ran, with no phantom steps from the other', () => {
    const branches = buildLaneBranches(FETCH_STEPS.map(step => run(step)))

    expect(branches.map(branch => branch.id)).toEqual(['fetch'])
    expect(branches[0]!.links.map(link => link.processName)).toEqual([...FETCH_STEPS])
    expect(branches[0]!.links.every(link => link.run !== null)).toBe(true)
  })

  it('renders both branches for a Full pass', () => {
    const branches = buildLaneBranches(PIPELINE_CHAIN.map(step => run(step)))

    expect(branches.map(branch => branch.id)).toEqual(['fetch', 'aggregate'])
    expect(branches.flatMap(branch => branch.links)).toHaveLength(PIPELINE_CHAIN.length)
  })

  it('marks the steps of a partially-run lane as notRun without dropping them', () => {
    const branches = buildLaneBranches([run('MatchTeamPositionCorrection'), run('MainAnalysis')])

    expect(branches).toHaveLength(1)
    const [branch] = branches
    expect(branch!.links).toHaveLength(AGGREGATE_STEPS.length)
    expect(branch!.links.filter(link => link.outcome === 'notRun')).toHaveLength(
      AGGREGATE_STEPS.length - 2,
    )
  })

  it('shows the bare pipeline shape when nothing has run', () => {
    const branches = buildLaneBranches([])

    expect(branches.map(branch => branch.id)).toEqual(['fetch', 'aggregate'])
    expect(branches.every(branch => !branch.ran)).toBe(true)
    expect(branches.flatMap(branch => branch.links).every(link => link.outcome === 'notRun')).toBe(true)
  })

  it('keeps a run whose process belongs to no lane, in a trailing branch', () => {
    const branches = buildLaneBranches([run('MatchIngestion'), run('SomeUndeclaredStep')])

    expect(branches.map(branch => branch.id)).toEqual(['fetch', 'other'])
    expect(branches.at(-1)!.links.map(link => link.processName)).toEqual(['SomeUndeclaredStep'])
  })

  it('takes the most alarming status as the lane outcome', () => {
    const failed = buildLaneBranches([
      run('LadderSync'),
      run('Discovery', { status: 'Failed' }),
      run('MatchIngestion', { status: 'Skipped' }),
    ])
    expect(failed[0]!.outcome).toBe('Failed')

    const running = buildLaneBranches([
      run('LadderSync', { status: 'Failed' }),
      run('Discovery', { status: 'Running', finishedAtUtc: null }),
    ])
    expect(running[0]!.outcome).toBe('Running')
  })

  it('measures the lane from its first start to its last finish, and stays silent while in flight', () => {
    const finished = buildLaneBranches([
      run('LadderSync', { startedAtUtc: '2026-09-02T18:00:00.000Z', finishedAtUtc: '2026-09-02T18:01:00.000Z' }),
      // A gap between the two runs is part of the lane's wall-clock span: the header
      // answers "how long did the lane take", not "how long was it busy".
      run('MatchIngestion', { startedAtUtc: '2026-09-02T18:05:00.000Z', finishedAtUtc: '2026-09-02T18:07:30.000Z' }),
    ])
    expect(finished[0]!.startedAtUtc).toBe('2026-09-02T18:00:00.000Z')
    expect(finished[0]!.durationMs).toBe(450_000)

    const inFlight = buildLaneBranches([
      run('LadderSync'),
      run('MatchIngestion', { status: 'Running', finishedAtUtc: null }),
    ])
    expect(inFlight[0]!.durationMs).toBeNull()

    // The API mirrors a running run's start into `finishedAtUtc` so the iteration's
    // last-activity stamp advances; the status, not the timestamps, says in-flight.
    const mirrored = buildLaneBranches([
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
    const branches = buildLaneBranches([
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
    const lanes = pickCurrentLanes([
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
    const lanes = pickCurrentLanes([{ runs: [run('LadderSync'), run('MatchIngestion')] }])

    expect(lanes.map(lane => lane.branch.id)).toEqual(['fetch', 'aggregate'])
    const aggregate = lanes[1]!
    expect(aggregate.branch.ran).toBe(false)
    expect(aggregate.branch.outcome).toBe('notRun')
    expect(aggregate.isRunning).toBe(false)
  })

  it('shows every lane not-run when nothing has been recorded at all', () => {
    const lanes = pickCurrentLanes([])

    expect(lanes.map(lane => lane.branch.id)).toEqual(['fetch', 'aggregate'])
    expect(lanes.every(lane => !lane.branch.ran)).toBe(true)
  })

  it('reports a Full pass as one running lane, not two', () => {
    // The iteration is running, but only the branch holding the running step is.
    const lanes = pickCurrentLanes([{
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
    const branches = buildLaneBranches([
      run('MatchDataRetention', { jobMode: 'MatchDataRetentionOnly' }),
    ])

    expect(branches).toHaveLength(1)
    expect(branches[0]!.links.map(link => link.processName)).toEqual(['MatchDataRetention'])
    expect(branches[0]!.label).toBe('MatchDataRetentionOnly')
  })

  it('does not narrow a composite mode', () => {
    // FetchLane is a sequence, so a pass that has only reached LadderSync really has
    // seven steps still to come and must keep showing them.
    const branches = buildLaneBranches([run('LadderSync', { jobMode: 'FetchLane' })])

    expect(branches).toHaveLength(1)
    expect(branches[0]!.links.length).toBeGreaterThan(1)
  })

  it('falls back to the lane rendering when no mode was recorded', () => {
    // Runs written before the ingestor stamped the mode: guessing "one-off" from a
    // single run would hide the rest of a lane that had merely just started.
    const branches = buildLaneBranches([run('MatchDataRetention')])

    expect(branches[0]!.links.length).toBeGreaterThan(1)
  })
})
