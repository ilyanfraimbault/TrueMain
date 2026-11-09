import type { SeedRequestReadModel, SeedRequestStatus } from '#site-shared/contract/api'
import { TERMINAL_SEED_STATUSES } from '~~/shared/types/ops'

/**
 * Where the Add-mains single seed stands (#2471): one value instead of five refs
 * (`submitting`, `tracked`, `submitError`, `polling`, `polledOut`) whose
 * combinations the form had to untangle.
 *
 * - `idle`: nothing submitted.
 * - `submitting`: the POST is in flight.
 * - `rejected`: the POST failed before an id came back; `error` is its message.
 * - `polling`: the request is tracked and its status re-read every few seconds.
 * - `settled`: it reached a terminal status (`Ingested` or `Failed`).
 * - `timedOut`: the safety deadline passed first — it goes on in the background.
 */
export type SeedTracking =
  | { s: 'idle' }
  | { s: 'submitting' }
  | { s: 'rejected', error: string }
  | { s: 'polling', request: SeedRequestReadModel }
  | { s: 'settled', request: SeedRequestReadModel }
  | { s: 'timedOut', request: SeedRequestReadModel }

export function isTerminalSeedStatus(status: SeedRequestStatus): boolean {
  return TERMINAL_SEED_STATUSES.includes(status)
}

/** The request a state tracks, if any. */
export function trackedRequest(state: SeedTracking): SeedRequestReadModel | null {
  return state.s === 'polling' || state.s === 'settled' || state.s === 'timedOut' ? state.request : null
}

/** A status read for the tracked request: terminal settles, anything else keeps polling. */
export function seedStatusRead(request: SeedRequestReadModel): SeedTracking {
  return isTerminalSeedStatus(request.status) ? { s: 'settled', request } : { s: 'polling', request }
}

/**
 * One step of the stepper (Queued → Resolving Riot ID → Account queued).
 * `failed` is drawn on the step the request broke on: the resolve step — the
 * account was never queued, so the last step stays `todo`.
 */
export type SeedStepState = 'done' | 'active' | 'failed' | 'todo'

export const SEED_STEPS = [
  { key: 'Pending', label: 'Queued' },
  { key: 'Resolving', label: 'Resolving Riot ID' },
  { key: 'Ingested', label: 'Account queued' },
] as const

const STEP_STATES: Record<SeedRequestStatus, readonly SeedStepState[]> = {
  Pending: ['active', 'todo', 'todo'],
  Resolving: ['done', 'active', 'todo'],
  Ingested: ['done', 'done', 'done'],
  Failed: ['done', 'failed', 'todo'],
}

export function seedStepStates(status: SeedRequestStatus): readonly SeedStepState[] {
  return STEP_STATES[status]
}

/** Each step state drawn once: the dot, the label, the connector after it. */
export const SEED_STEP_STYLE: Record<SeedStepState, { dot: string, label: string, connector: string }> = {
  done: { dot: 'bg-success/10 text-success ring-success/30', label: 'text-default', connector: 'bg-success/40' },
  active: { dot: 'bg-primary/10 text-primary ring-primary/30', label: 'text-default', connector: 'bg-default' },
  failed: { dot: 'bg-error/10 text-error ring-error/30', label: 'text-error', connector: 'bg-default' },
  todo: { dot: 'bg-elevated text-dimmed ring-default', label: 'text-dimmed', connector: 'bg-default' },
}
