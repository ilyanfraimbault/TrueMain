import { mkdirSync, readdirSync, readFileSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'

const SHARED_CLAIM = 'shared-claimed'
const SHARED_DONE = 'shared-done'
const WORKER_PREFIX = 'worker-'

/**
 * What every worker of this container has warmed so far (#2425), as files: the edge's probe
 * lands on any worker, and each has to answer for all of them, with nothing else between them
 * (Nitro's cluster preset owns the primary process).
 *
 * The directory is named after the primary's start time: a container that restarts keeps its
 * `/tmp`, often its process ids too, and must not read a previous life's flags as its own. A
 * worker the primary forks again after a crash finds the flags of the same life, so the site
 * stays open while it warms itself.
 */
function stateDir(): string {
  let life = String(process.ppid)
  try {
    // Field 22 of /proc/<pid>/stat, counted from after the command name, which may hold spaces.
    const stat = readFileSync(`/proc/${process.ppid}/stat`, 'utf8')
    life += `-${stat.slice(stat.lastIndexOf(')') + 2).split(' ')[19]}`
  }
  catch {
    // Not Linux: the process id alone will do outside the container.
  }
  return join(tmpdir(), 'truemain-warmup', life)
}

function ensureDir(): string {
  const dir = stateDir()
  mkdirSync(dir, { recursive: true })
  return dir
}

export function markWorkerWarm(): void {
  writeFileSync(join(ensureDir(), `${WORKER_PREFIX}${process.pid}`), '')
}

/** True for the one worker that gets to warm what they all share. `mkdir` is the atomic part. */
export function claimSharedWarmup(): boolean {
  try {
    mkdirSync(join(ensureDir(), SHARED_CLAIM))
    return true
  }
  catch {
    return false
  }
}

export function markSharedWarm(): void {
  writeFileSync(join(ensureDir(), SHARED_DONE), '')
}

/** Whether every worker and what they share are done, or the budget has run out. */
export function isSiteWarm(workers: number, budgetSeconds: number): boolean {
  if (process.uptime() >= budgetSeconds) return true
  const names = readdirSync(ensureDir())
  return names.includes(SHARED_DONE)
    && names.filter(name => name.startsWith(WORKER_PREFIX)).length >= workers
}

/** How many workers this container runs, as `node-cluster` reads it. */
export function clusterWorkers(): number {
  const workers = Number.parseInt(process.env.NITRO_CLUSTER_WORKERS ?? '', 10)
  return Number.isFinite(workers) && workers > 0 ? workers : 1
}
