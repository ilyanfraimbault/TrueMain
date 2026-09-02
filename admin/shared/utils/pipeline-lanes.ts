import type { PipelineLaneId, ProcessRun, ProcessRunStatus } from '~~/shared/types/ops'
import { PIPELINE_LANES } from '~~/shared/types/ops'

/**
 * Grouping an iteration's runs into the lanes that produced them.
 *
 * The chain used to be one flat list of the 20 steps, annotating everything the
 * iteration did not contain as `notRun`. That was right while a pass ran every
 * step; since #1362 an iteration belongs to ONE lane, so the flat rendering drew
 * the other lane's dozen steps as phantom grey chips on every pass. Grouping by
 * lane says the true thing instead — this pass is the fetch lane, and these are
 * its eight steps — and a `Full` iteration (prod's topology until the split
 * ships there) simply lights up both branches.
 */

/**
 * Per-process outcome within one iteration. `notRun` covers both a process that
 * has not started yet in the current pass and one that was skipped this pass.
 */
export type ChainOutcome = ProcessRunStatus | 'notRun'

export interface ChainLink {
  processName: string
  outcome: ChainOutcome
  run: ProcessRun | null
}

/** A lane's steps within one iteration, plus what the lane as a whole did. */
export interface LaneBranch {
  /** `other` is the catch-all for a run whose process is in no lane. */
  id: PipelineLaneId | 'other'
  label: string
  description: string
  links: ChainLink[]
  /** True when at least one of the branch's steps actually recorded a run. */
  ran: boolean
  /** The branch's own status — see {@link OUTCOME_PRECEDENCE}. */
  outcome: ChainOutcome
  /** Earliest start across the branch's runs, or null when none ran. */
  startedAtUtc: string | null
  /**
   * Wall-clock span of the branch: its earliest start to its latest finish. Null
   * while a run is still in flight (no finish to measure to) or when none ran.
   */
  durationMs: number | null
}

/**
 * How a branch inherits a status from its steps: the most alarming one wins, and
 * a lane with a step still going is `Running` whatever else it holds. `Skipped`
 * sits below `Success` so a lane that did real work is not reported as skipped
 * because its last step had nothing to do.
 */
const OUTCOME_PRECEDENCE: readonly ChainOutcome[] = [
  'Running',
  'Failed',
  'Abandoned',
  'Success',
  'Skipped',
  'notRun',
]

const LANE_BY_PROCESS: ReadonlyMap<string, PipelineLaneId> = new Map(
  PIPELINE_LANES.flatMap(lane => lane.steps.map(step => [step, lane.id] as const)),
)

/** The lane a process belongs to, or null for a name no lane declares. */
export function laneForProcess(processName: string): PipelineLaneId | null {
  return LANE_BY_PROCESS.get(processName) ?? null
}

/**
 * Build the lane branches for one iteration.
 *
 * Only the lanes that actually ran are returned, which is what makes a one-lane
 * iteration render as one branch — except for the empty case (no runs at all,
 * i.e. nothing has been recorded yet), which returns every lane fully `notRun`
 * so the panel still shows the shape of the pipeline instead of a blank box.
 *
 * A run whose process is in no lane lands in a trailing `other` branch rather
 * than being dropped: an ingestor step added without its entry here must still
 * be visible, under its raw name, the way the flat chain used to append it.
 */
export function buildLaneBranches(runs: readonly ProcessRun[]): LaneBranch[] {
  // Latest run wins for a process that ran twice in the pass (a retry), matching
  // what the flat chain did.
  const byName = new Map(runs.map(run => [run.processName, run]))

  // A pass that ran one process on purpose is not a lane that stopped after one
  // step, and the two are indistinguishable by their contents — which is why the
  // ingestor records the mode it opened the pass with. Drawing such a run against a
  // whole lane would put eleven "Not run" chips beside it and claim they were
  // skipped, the same misreading the lane split was made to remove, just narrower.
  const singleProcessMode = singleProcessModeOf(runs)
  if (singleProcessMode) {
    const links: ChainLink[] = runs.map(run => ({
      processName: run.processName,
      outcome: run.status,
      run,
    }))
    return [toBranch(
      laneForProcess(runs[0]!.processName) ?? 'other',
      singleProcessMode,
      'A single process run on its own, outside the lane cadences.',
      links,
    )]
  }

  const branches = PIPELINE_LANES.map((lane) => {
    const links: ChainLink[] = lane.steps.map((processName) => {
      const run = byName.get(processName) ?? null
      return { processName, outcome: run?.status ?? 'notRun', run }
    })
    return toBranch(lane.id, lane.label, lane.description, links)
  })

  const unknown = runs.filter(run => laneForProcess(run.processName) === null)
  if (unknown.length > 0) {
    const seen = new Set<string>()
    const links: ChainLink[] = []
    for (const run of unknown) {
      if (seen.has(run.processName)) {
        continue
      }
      seen.add(run.processName)
      const latest = byName.get(run.processName) ?? run
      links.push({ processName: latest.processName, outcome: latest.status, run: latest })
    }
    branches.push(toBranch(
      'other',
      'Unassigned steps',
      'Steps this admin build does not know a lane for — they ran, so they are shown under their raw name.',
      links,
    ))
  }

  const ranBranches = branches.filter(branch => branch.ran)
  return ranBranches.length > 0 ? ranBranches : branches
}

/**
 * The recorded `Job:Mode` when this pass was a deliberate single-process run, else
 * null. `Full`, `FetchLane` and `AggregateLane` are the composite modes — they
 * expand to a sequence, so they are drawn against their lanes. Anything else names
 * one process, and a pass with no recorded mode (written before the ingestor
 * stamped it) is left to the lane rendering rather than guessed at.
 */
function singleProcessModeOf(runs: readonly ProcessRun[]): string | null {
  if (runs.length === 0) {
    return null
  }

  const mode = runs.find(run => run.jobMode)?.jobMode
  if (!mode || COMPOSITE_JOB_MODES.has(mode)) {
    return null
  }

  return mode
}

const COMPOSITE_JOB_MODES: ReadonlySet<string> = new Set(['Full', 'FetchLane', 'AggregateLane'])

function toBranch(
  id: LaneBranch['id'],
  label: string,
  description: string,
  links: ChainLink[],
): LaneBranch {
  const runs = links.map(link => link.run).filter((run): run is ProcessRun => run !== null)

  const startedAtUtc = runs.length === 0
    ? null
    : runs.reduce((earliest, run) => (run.startedAtUtc < earliest ? run.startedAtUtc : earliest), runs[0]!.startedAtUtc)

  // Measured from the branch's own runs rather than summed from their durations:
  // the sum would hide a gap between two steps, and the question a lane header
  // answers is how long the lane took, not how long it was busy.
  //
  // A lane still in flight has no span to report — and it cannot be detected by a
  // missing `finishedAtUtc`, because the API mirrors a running run's start into it
  // so the iteration's "last activity" still advances. The run's status is the
  // only honest signal, so a `Running` step makes the whole branch's duration null
  // rather than a 0 ms that reads as "instant".
  const finishes = runs.map(run => run.finishedAtUtc).filter((at): at is string => at !== null)
  const inFlight = links.some(link => link.outcome === 'Running')
  const durationMs = startedAtUtc === null || inFlight || finishes.length !== runs.length
    ? null
    : Math.max(0, Date.parse(finishes.reduce((latest, at) => (at > latest ? at : latest), finishes[0]!)) - Date.parse(startedAtUtc))

  return {
    id,
    label,
    description,
    links,
    ran: runs.length > 0,
    outcome: OUTCOME_PRECEDENCE.find(candidate => links.some(link => link.outcome === candidate)) ?? 'notRun',
    startedAtUtc,
    durationMs,
  }
}

/** One lane as it stands right now, for the live tree at the top of the panel. */
export interface CurrentLane {
  branch: LaneBranch
  isRunning: boolean
}

/**
 * The current state of every lane, from the most recent iterations (newest first).
 *
 * Each lane is shown at *its own* newest iteration, which is why this takes a
 * handful of them rather than one: the lanes run at their own cadences, so the
 * slower one's newest pass sits a few positions down a newest-first list, and
 * "the newest iteration" would show one lane and hide the other.
 *
 * Every lane is always returned. A lane absent from the window has not run
 * recently — a stalled or dead lane is exactly what this panel is read for — so
 * it stays on screen, marked not-run, instead of vanishing.
 */
export function pickCurrentLanes(
  iterations: readonly { runs: ProcessRun[] }[],
): CurrentLane[] {
  const byLane = new Map<string, CurrentLane>(
    buildLaneBranches([]).map(branch => [branch.id, { branch, isRunning: false }]),
  )

  for (const iteration of iterations) {
    for (const branch of buildLaneBranches(iteration.runs)) {
      // First iteration that ran the lane wins — the list is newest-first.
      if (!branch.ran || byLane.get(branch.id)?.branch.ran) {
        continue
      }
      byLane.set(branch.id, {
        branch,
        // The iteration's own running flag would mark BOTH branches of a `Full`
        // pass as running because one of them is; the branch's own outcome is the
        // honest per-lane answer.
        isRunning: branch.outcome === 'Running',
      })
    }
  }

  // Lanes assembled from different iterations still read in pipeline order,
  // with the catch-all branch last.
  const rank = new Map<string, number>(PIPELINE_LANES.map((lane, index) => [lane.id, index]))
  return [...byLane.values()].sort(
    (a, b) => (rank.get(a.branch.id) ?? PIPELINE_LANES.length) - (rank.get(b.branch.id) ?? PIPELINE_LANES.length),
  )
}
