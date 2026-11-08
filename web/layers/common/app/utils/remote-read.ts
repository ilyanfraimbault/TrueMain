import { fetchErrorStatus } from './errors'

/**
 * Where one remote read stands (#2476): one value, so the impossible
 * combinations the flag bundle it replaces could hold — "not found" while the
 * next request loads, an error kept after a successful retry, "loading" stuck
 * on after the name tag emptied — cannot be written down at all.
 *
 * - `waiting`: nothing asked yet — the read is gated, or the component has not
 *   mounted. Reads as a cold load, never as an empty answer.
 * - `cleared`: there is nothing to ask about (an empty name tag).
 * - `loading`: a request is in flight; `previous` is what stays on screen
 *   meanwhile, `null` for a cold load.
 * - `ready`: the answer.
 * - `notFound`: the API answered 404 — and only a 404. A 5xx, a 429 or a
 *   network failure is an `error`, so an outage never reads as an unknown
 *   player (and never answers a crawler with a 404).
 * - `error`: any other failure; `previous` is the last answer, kept on screen.
 */
export type RemoteRead<T> =
  | { kind: 'waiting' }
  | { kind: 'cleared' }
  | { kind: 'loading', previous: T | null }
  | { kind: 'ready', data: T }
  | { kind: 'notFound' }
  | { kind: 'error', error: unknown, previous: T | null }

/** The answer a read can still show: the current one, or the one a refetch or a failure kept. */
export function shownData<T>(read: RemoteRead<T>): T | null {
  switch (read.kind) {
    case 'ready': return read.data
    case 'loading':
    case 'error': return read.previous
    default: return null
  }
}

/** A request starts: whatever was on screen stays there until it lands. */
export function startLoading<T>(read: RemoteRead<T>, options: { fresh?: boolean } = {}): RemoteRead<T> {
  return { kind: 'loading', previous: options.fresh ? null : shownData(read) }
}

/** A request failed: a 404 is the `notFound` answer, anything else an `error` keeping the last answer. */
export function failed<T>(read: RemoteRead<T>, error: unknown): RemoteRead<T> {
  return isNotFoundError(error)
    ? { kind: 'notFound' }
    : { kind: 'error', error, previous: shownData(read) }
}

/** Nothing on screen yet and something on its way: draw the skeleton. */
export function isColdLoad(read: RemoteRead<unknown>): boolean {
  return read.kind === 'waiting' || (read.kind === 'loading' && read.previous === null)
}

/** The only failure that means "this does not exist". */
export function isNotFoundError(error: unknown): boolean {
  return fetchErrorStatus(error) === 404
}
