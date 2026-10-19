// The ops scripts kept outside the repo report their runs to `POST /ops/process-runs`
// (#2119), so their runs sit in `process_runs` next to the Ingestor's. This module knows
// which processes those are and reads the summary convention they share: a `steps` list
// (one entry per phase, with its own counters), the `currentStep`, and a `progress`
// gauge for the phase in flight. Everything else in the summary is the run's report and
// is rendered as-is by `ProcessSummaryView`.
import { isPlainObject } from './process-summary'

export interface ScraperProcess {
  /** The `processName` the script reports under (the API's allow-list). */
  name: string
  label: string
  description: string
}

export const SCRAPER_PROCESSES: readonly ScraperProcess[] = [
  {
    name: 'PersonaScraper',
    label: 'Persona scraper',
    description: 'Known players from Leaguepedia, TrackingThePros and DeepLoL, sent to the personas intake.',
  },
  {
    name: 'OtpSeeder',
    label: 'OTP seeder',
    description: 'One-trick accounts from public OTP sites, seeded when TrueMain does not know them.',
  },
]

export type ScraperStepStatus = 'running' | 'done' | 'failed' | 'skipped'

export interface ScraperStep {
  name: string
  /** Display name sent by the script (`DeepLoL`); the UI falls back to the humanised `name`. */
  label: string | null
  status: ScraperStepStatus
  startedAtUtc: string | null
  finishedAtUtc: string | null
  /** The phase's own numbers (requests, records kept, …); scalars only. */
  counters: Record<string, number | string>
}

export interface ScraperProgress {
  done: number
  total: number | null
  label: string | null
}

export interface ScraperSummary {
  steps: ScraperStep[]
  currentStep: string | null
  progress: ScraperProgress | null
  /** The summary minus the three keys above: the run's report. */
  report: Record<string, unknown>
}

const STEP_STATUSES: ReadonlySet<string> = new Set<ScraperStepStatus>(['running', 'done', 'failed', 'skipped'])

function asString(value: unknown): string | null {
  return typeof value === 'string' && value.trim() !== '' ? value : null
}

function asCount(value: unknown): number | null {
  return typeof value === 'number' && Number.isFinite(value) && value >= 0 ? value : null
}

function parseStep(raw: unknown): ScraperStep | null {
  if (!isPlainObject(raw)) {
    return null
  }
  const name = asString(raw.name)
  if (!name) {
    return null
  }
  const status = typeof raw.status === 'string' && STEP_STATUSES.has(raw.status)
    ? raw.status as ScraperStepStatus
    : 'done'
  const counters: Record<string, number | string> = {}
  if (isPlainObject(raw.counters)) {
    for (const [key, value] of Object.entries(raw.counters)) {
      if (typeof value === 'number' || typeof value === 'string') {
        counters[key] = value
      }
    }
  }
  return {
    name,
    label: asString(raw.label),
    status,
    startedAtUtc: asString(raw.startedAtUtc),
    finishedAtUtc: asString(raw.finishedAtUtc),
    counters,
  }
}

function parseProgress(raw: unknown): ScraperProgress | null {
  if (!isPlainObject(raw)) {
    return null
  }
  const done = asCount(raw.done)
  if (done === null) {
    return null
  }
  const total = asCount(raw.total)
  return { done, total: total && total > 0 ? total : null, label: asString(raw.label) }
}

/**
 * Split a scraper run's summary into its steps, current step, progress and report. A
 * summary that does not follow the convention (an old run, a free-form report) yields no
 * steps and keeps every key in `report`, so nothing a script sent is ever hidden.
 */
export function parseScraperSummary(summary: unknown): ScraperSummary {
  if (!isPlainObject(summary)) {
    return { steps: [], currentStep: null, progress: null, report: {} }
  }
  const { steps, currentStep, progress, ...report } = summary
  return {
    steps: Array.isArray(steps) ? steps.map(parseStep).filter((s): s is ScraperStep => s !== null) : [],
    currentStep: asString(currentStep),
    progress: parseProgress(progress),
    report,
  }
}

/** Share of a progress gauge done, in [0, 100], or null when the total is unknown. */
export function progressPercent(progress: ScraperProgress | null): number | null {
  if (progress?.total == null) {
    return null
  }
  return Math.min(100, Math.round((progress.done / progress.total) * 100))
}

/** Whether a process name is one of the reporting ops scripts. */
export function isScraperProcess(processName: string): boolean {
  return SCRAPER_PROCESSES.some(scraper => scraper.name === processName)
}
