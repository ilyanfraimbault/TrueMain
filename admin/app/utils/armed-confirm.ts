/**
 * A two-click confirmation (#2471): the first click on a destructive action arms
 * it, the second sends it. One union for the seven known-players actions that used
 * to copy it with their own flags and their own disarm rules.
 *
 * - `idle`: nothing armed.
 * - `armed`: `key` waits for its second click; a click on another key arms that one.
 * - `sending`: `key` was sent; every click is ignored until it settles.
 */
export type ArmedConfirm<K extends string> =
  | { s: 'idle' }
  | { s: 'armed', key: K }
  | { s: 'sending', key: K }

export interface ArmedPress<K extends string> {
  state: ArmedConfirm<K>
  /** True when this click sends the action. */
  send: boolean
}

/**
 * A click on `key`. An action that needs no confirmation (`confirm: false`) sends on
 * its first click; any other sends only from its own `armed` state.
 */
export function pressArmed<K extends string>(state: ArmedConfirm<K>, key: K, confirm = true): ArmedPress<K> {
  if (state.s === 'sending') {
    return { state, send: false }
  }
  if (!confirm || (state.s === 'armed' && state.key === key)) {
    return { state: { s: 'sending', key }, send: true }
  }
  return { state: { s: 'armed', key }, send: false }
}

/** The key waiting for its second click, if any. */
export function armedKey<K extends string>(state: ArmedConfirm<K>): K | null {
  return state.s === 'armed' ? state.key : null
}
