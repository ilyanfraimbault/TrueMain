import type { ComputedRef, MaybeRefOrGetter, Ref } from 'vue'
// Explicit imports rather than Nuxt auto-imports: this composable is unit tested
// outside the Nuxt context (`tests/refetch-fallback`).
import { computed, shallowRef, toValue, watch } from 'vue'

export type FetchStatus = 'idle' | 'pending' | 'success' | 'error'

export interface RefetchFallbackSource<T> {
  data: Ref<T>
  status: Ref<FetchStatus>
  error: Ref<unknown>
}

export interface RefetchFallbackOptions {
  /**
   * What the payload is *about*, beyond the filters a refetch changes: the
   * champion a champion page shows, the champion a matchup is for. A payload
   * fetched under another scope is never substituted — it would put one
   * champion's build under another's name. Omitted = one scope for the whole
   * source (a listing whose every input is a filter).
   */
  scope?: MaybeRefOrGetter<string>
  /**
   * Called once per failed refetch that left the previous payload on screen —
   * the caller's action toast (#1668). Never called for a failure with nothing
   * to fall back on: that one is the inline alert's, and only the alert's.
   */
  onStaleFailure?: (error: unknown) => void
}

export interface RefetchFallback<T> {
  /** The payload to draw: the current one, or the last good one when a refetch failed. */
  data: ComputedRef<T>
  /** A failure with nothing to show in its place — the region's inline alert. */
  error: ComputedRef<unknown>
  /** A failure the previous payload is standing in for — the "still stale" notice. */
  staleError: ComputedRef<unknown>
}

/**
 * Keeps the last successful payload on screen when a refetch fails (#1668).
 *
 * Nuxt's `useAsyncData` resets `data` to its `default()` when a request throws,
 * so a filter or pager click whose request failed used to empty the table the
 * reader was already looking at. This remembers the last payload a request
 * *succeeded* with and draws it in place of the reset one, and splits the
 * failure in two: an `error` when there was nothing to keep (the first load —
 * unchanged, the inline alert), and a `staleError` when the previous payload is
 * standing in (a notice beside the content, plus one action toast).
 *
 * Unlike the admin's `useLastGoodPayload`, which only ever re-shows a payload
 * fetched for the *same* key, this deliberately keeps a payload fetched under
 * the previous filters: that is the point — the reader keeps the rows they were
 * reading — and the stale notice says so. `scope` is the line it does not cross.
 *
 * While a request is in flight the last good payload is drawn too, rather than
 * whatever the new key's entry was seeded with: after a failure that seed is
 * the reset default, so the next filter click would otherwise flash an empty
 * table under the loading bar.
 */
export function useRefetchFallback<T>(
  source: RefetchFallbackSource<T>,
  options: RefetchFallbackOptions = {},
): RefetchFallback<T> {
  const scopeOf = () => toValue(options.scope) ?? ''
  const lastGood = shallowRef<{ scope: string, value: T } | null>(null)

  // `data` too, not only `status`: a key switch onto a cached entry stays
  // `success` throughout, and only its `data` moves.
  watch([source.status, source.data], ([status, data]) => {
    if (status !== 'success') return
    // A settled empty answer (`null`: "no data for this champion") is the
    // answer — a later failure must not resurrect what came before it.
    lastGood.value = data === null || data === undefined ? null : { scope: scopeOf(), value: data }
  }, { immediate: true })

  const fallback = computed(() => {
    const last = lastGood.value
    return last && last.scope === scopeOf() ? last : null
  })

  const data = computed<T>(() => {
    if (source.status.value === 'success') return source.data.value
    return fallback.value ? fallback.value.value : source.data.value
  })
  const error = computed(() => (fallback.value ? null : source.error.value ?? null))
  const staleError = computed(() => (fallback.value ? source.error.value ?? null : null))

  // Once per failure: a key flipping back onto an entry that still holds its
  // old error must not toast that error a second time.
  const reported = new WeakSet<object>()
  watch(staleError, (failure) => {
    if (failure === null || typeof failure !== 'object' || reported.has(failure)) return
    reported.add(failure)
    options.onStaleFailure?.(failure)
  })

  return { data, error, staleError }
}
