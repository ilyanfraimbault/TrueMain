/**
 * Where one fetch for one entity stands (#2471): which entity was asked for travels
 * with every phase, so an answer can never be drawn — or acted on — under another
 * entity's name. The flag bundle it replaces (`detail` + `pending` + `error` + a
 * separate open id) could hold A's answer while B was open, or B's `pending`
 * cleared by A's late answer.
 *
 * - `idle`: nothing asked.
 * - `loading`: a request for `id` is in flight; `previous` is the last answer for
 *   the SAME id (a reload), kept on screen meanwhile, `null` for a first load.
 * - `error`: the request for `id` failed; `previous` as above.
 * - `ready`: the answer for `id`.
 *
 * Out-of-order answers are dropped by the composable (`useKeyedFetch`), which owns
 * the request token; the transitions below are the pure part it applies.
 */
export type KeyedFetch<K, T> =
  | { s: 'idle' }
  | { s: 'loading', id: K, previous: T | null }
  | { s: 'error', id: K, error: unknown, previous: T | null }
  | { s: 'ready', id: K, value: T }

export type SameKey<K> = (a: K, b: K) => boolean

/** The answer this state can still show, whatever is in flight. */
export function keyedValue<T>(state: KeyedFetch<unknown, T>): T | null {
  switch (state.s) {
    case 'ready': return state.value
    case 'loading':
    case 'error': return state.previous
    default: return null
  }
}

/** The entity the state is about, or `null` when idle. */
export function keyedId<K>(state: KeyedFetch<K, unknown>): K | null {
  return state.s === 'idle' ? null : state.id
}

/**
 * A request for `id` starts. The answer on screen stays only when it is `id`'s own
 * (a reload); another entity's answer is dropped at once, never shown under `id`.
 */
export function startKeyed<K, T>(state: KeyedFetch<K, T>, id: K, same: SameKey<K> = Object.is): KeyedFetch<K, T> {
  const current = keyedId(state)
  const previous = current !== null && same(current, id) ? keyedValue(state) : null
  return { s: 'loading', id, previous }
}

/** The request for `id` failed: the same-id answer it reloaded stays, flagged by the error. */
export function failKeyed<K, T>(state: KeyedFetch<K, T>, id: K, error: unknown, same: SameKey<K> = Object.is): KeyedFetch<K, T> {
  const current = keyedId(state)
  const previous = current !== null && same(current, id) ? keyedValue(state) : null
  return { s: 'error', id, error, previous }
}
