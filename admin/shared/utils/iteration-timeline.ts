import type { ProcessIteration, ProcessRun, ProcessRunStatus } from '~~/shared/types/ops'
import { laneForProcess } from '~~/shared/utils/pipeline-lanes'

/**
 * Where an iteration's time went, for the compact rows of the recent-iterations list.
 *
 * The list used to draw every pass as its full chain of named chips, which made each
 * row a paragraph to read and never said how long a step took. These helpers answer
 * that from the runs the API already returns — no extra request.
 */

/** What kind of pass an iteration was, read from the `Job:Mode` it ran under. */
export type IterationKindId = 'fetch' | 'aggregate' | 'full' | 'single'

export interface IterationKind {
  id: IterationKindId
  label: string
}

const KIND_BY_MODE: Readonly<Record<string, IterationKind>> = {
  FetchLane: { id: 'fetch', label: 'Fetch' },
  // The fetch lane run as permanent workers (#1457): its maintenance passes and its workers'
  // batches are both fetch work.
  FetchWorkers: { id: 'fetch', label: 'Fetch' },
  AggregateLane: { id: 'aggregate', label: 'Aggregate' },
  Full: { id: 'full', label: 'Full' },
}

/**
 * The pass's kind. A single-process mode keeps its raw name — it is the identifier an
 * operator set by hand. A pass recorded before the mode was stamped falls back to the
 * lane of its processes, and to `single` under its first process name when they span
 * no single lane.
 */
export function iterationKind(iteration: Pick<ProcessIteration, 'jobMode' | 'runs'>): IterationKind {
  const mode = iteration.jobMode ?? iteration.runs.find(run => run.jobMode)?.jobMode ?? null
  if (mode) {
    return KIND_BY_MODE[mode] ?? { id: 'single', label: mode }
  }

  const lanes = new Set(iteration.runs.map(run => laneForProcess(run.processName)))
  if (lanes.size === 1) {
    const lane = [...lanes][0]
    if (lane) {
      return lane === 'fetch' ? KIND_BY_MODE.FetchLane! : KIND_BY_MODE.AggregateLane!
    }
  }
  if (lanes.size === 2 && !lanes.has(null)) {
    return KIND_BY_MODE.Full!
  }
  return { id: 'single', label: iteration.runs[0]?.processName ?? 'Unknown' }
}

/** The wall-clock span of a pass: its start to its last recorded activity. */
export interface IterationSpan {
  startMs: number
  endMs: number
  durationMs: number
}

export function iterationSpan(iteration: Pick<ProcessIteration, 'startedAtUtc' | 'lastActivityAtUtc'>): IterationSpan {
  const startMs = Date.parse(iteration.startedAtUtc)
  const endMs = Math.max(startMs, Date.parse(iteration.lastActivityAtUtc))
  return { startMs, endMs, durationMs: endMs - startMs }
}

/**
 * The runs that took time, in the order they ran, for the row's segmented bar. A
 * skipped step decided in milliseconds that it had nothing to do; drawing it would
 * add a hairline to every bar and say nothing.
 */
export function timedRuns(runs: readonly ProcessRun[]): ProcessRun[] {
  return runs
    .filter(run => run.status !== 'Skipped' && run.durationMs > 0)
    .sort((a, b) => a.startedAtUtc.localeCompare(b.startedAtUtc))
}

/** How many of the pass's runs ended in each status. */
export function statusCounts(runs: readonly ProcessRun[]): Partial<Record<ProcessRunStatus, number>> {
  const counts: Partial<Record<ProcessRunStatus, number>> = {}
  for (const run of runs) {
    counts[run.status] = (counts[run.status] ?? 0) + 1
  }
  return counts
}

