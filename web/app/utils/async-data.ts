/**
 * True while a `useAsyncData` source hasn't produced data yet. `idle` is the
 * pre-fetch state from `useLazy*` before the client kicks off the request —
 * treat it as loading too, otherwise the SSR shell briefly renders the empty
 * state before the first fetch starts.
 */
export function isLoadingStatus(status: 'idle' | 'pending' | 'success' | 'error'): boolean {
  return status === 'idle' || status === 'pending'
}
