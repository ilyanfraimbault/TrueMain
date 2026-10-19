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
  /** Seconds left at the pace observed so far, null until the script can extrapolate it. */
  etaSeconds: number | null
}

/** What kind of run it is and how it was invoked. */
export interface ScraperRunInfo {
  /** `weekly`, `sweep`, `targeted`, `manual` — free text, the scripts decide. */
  kind: string | null
  version: string | null
  args: Record<string, unknown>
}

export type ScraperResultTone = 'success' | 'warning' | 'error'

export interface ScraperResultItem {
  label: string
  value: number | string
  tone: ScraperResultTone | null
}

/** One labelled block of headline numbers (e.g. "Personas intake": created, updated, …). */
export interface ScraperResultGroup {
  label: string
  items: ScraperResultItem[]
}

export interface ScraperSourceErrors {
  source: string
  /** The display name of the step of the same name, when there is one (`DeepLoL`). */
  label: string | null
  count: number
  /** The last few messages, oldest first. */
  last: string[]
  /** Why the source was dropped for the run (circuit breaker), when it was. */
  blocked: string | null
}

export interface ScraperSummary {
  steps: ScraperStep[]
  currentStep: string | null
  progress: ScraperProgress | null
  run: ScraperRunInfo | null
  results: ScraperResultGroup[]
  errors: ScraperSourceErrors[]
  /** The summary minus the keys above: the rest of the run's report. */
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
  return {
    done,
    total: total && total > 0 ? total : null,
    label: asString(raw.label),
    etaSeconds: asCount(raw.etaSeconds),
  }
}

function parseRun(raw: unknown): ScraperRunInfo | null {
  if (!isPlainObject(raw)) {
    return null
  }
  return {
    kind: asString(raw.kind),
    version: asString(raw.version),
    args: isPlainObject(raw.args) ? raw.args : {},
  }
}

const TONES: ReadonlySet<string> = new Set<ScraperResultTone>(['success', 'warning', 'error'])

function parseResults(raw: unknown): ScraperResultGroup[] {
  if (!Array.isArray(raw)) {
    return []
  }
  const groups: ScraperResultGroup[] = []
  for (const group of raw) {
    if (!isPlainObject(group) || !asString(group.label) || !Array.isArray(group.items)) {
      continue
    }
    const items = group.items.flatMap((entry): ScraperResultItem[] => {
      if (!isPlainObject(entry) || !asString(entry.label)) {
        return []
      }
      if (typeof entry.value !== 'number' && typeof entry.value !== 'string') {
        return []
      }
      const tone = typeof entry.tone === 'string' && TONES.has(entry.tone) ? entry.tone as ScraperResultTone : null
      return [{ label: entry.label as string, value: entry.value, tone }]
    })
    if (items.length) {
      groups.push({ label: group.label as string, items })
    }
  }
  return groups
}

function parseErrors(raw: unknown): ScraperSourceErrors[] {
  if (!isPlainObject(raw)) {
    return []
  }
  return Object.entries(raw).flatMap(([source, entry]): ScraperSourceErrors[] => {
    if (!isPlainObject(entry)) {
      return []
    }
    const last = Array.isArray(entry.last) ? entry.last.filter((m): m is string => typeof m === 'string') : []
    return [{ source, label: null, count: asCount(entry.count) ?? last.length, last, blocked: asString(entry.blocked) }]
  }).sort((a, b) => b.count - a.count)
}

/**
 * Split a scraper run's summary into its steps, current step, progress and report. A
 * summary that does not follow the convention (an old run, a free-form report) yields no
 * steps and keeps every key in `report`, so nothing a script sent is ever hidden.
 */
export function parseScraperSummary(summary: unknown): ScraperSummary {
  if (!isPlainObject(summary)) {
    return { steps: [], currentStep: null, progress: null, run: null, results: [], errors: [], report: {} }
  }
  const { steps, currentStep, progress, run, results, errors, ...report } = summary
  const parsedSteps = Array.isArray(steps) ? steps.map(parseStep).filter((s): s is ScraperStep => s !== null) : []
  const labels = new Map(parsedSteps.map(s => [s.name, s.label]))
  return {
    steps: parsedSteps,
    currentStep: asString(currentStep),
    progress: parseProgress(progress),
    run: parseRun(run),
    results: parseResults(results),
    errors: parseErrors(errors).map(e => ({ ...e, label: labels.get(e.source) ?? null })),
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

/** One run argument as the admin prints it: lists joined, booleans as on/off. */
export function formatRunArg(value: unknown): string {
  if (Array.isArray(value)) {
    return value.map(String).join(', ')
  }
  if (typeof value === 'boolean') {
    return value ? 'on' : 'off'
  }
  return String(value)
}

/** Seconds as a coarse remaining-time label: "~2h 10m", "~4m", "< 1m". */
export function formatEta(seconds: number | null | undefined): string | null {
  if (seconds === null || seconds === undefined) {
    return null
  }
  if (seconds < 60) {
    return '< 1m'
  }
  const minutes = Math.round(seconds / 60)
  const hours = Math.floor(minutes / 60)
  return hours ? `~${hours}h ${String(minutes % 60).padStart(2, '0')}m` : `~${minutes}m`
}
