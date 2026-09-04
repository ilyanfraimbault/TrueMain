import type { TruemainActivityResponse } from '~~/shared/types/activity'

/**
 * Fetch the activity-grid payload for the given <c>nameTag</c> (#927). Same
 * shape as <c>useTruemainRankHistory</c>: 404 surfaces as <c>notFound</c>
 * instead of throwing, and the request is client-only — the initial run hangs
 * off <c>onMounted</c> inside <c>useTruemainFetch</c>, which is what makes that
 * true rather than merely intended (#862).
 *
 * There is no mode argument. All three windows come back in one response because
 * they are foldings of the same games, so switching window is a local toggle
 * rather than a refetch — and two of them can never end up describing two
 * different snapshots of the same afternoon.
 */
export function useTruemainActivity(nameTag: MaybeRefOrGetter<string>) {
  const data = ref<TruemainActivityResponse | null>(null)

  const { isLoading, isInitialLoading, notFound, error } = useTruemainFetch<TruemainActivityResponse>(nameTag, {
    request: tag => $fetch<TruemainActivityResponse | null>(
      `/api/truemains/${encodeURIComponent(tag)}/activity`,
      { ignoreResponseError: true },
    ),
    // `ignoreResponseError` turns a 404 into a null body, so the shape check is
    // the only way to tell "not found" from "no data": a real payload always
    // carries the three windows, each with a bucket array.
    validate: (response): response is TruemainActivityResponse =>
      Boolean(
        response
        && typeof response === 'object'
        && Array.isArray(response.patch?.buckets)
        && Array.isArray(response.week?.buckets)
        && Array.isArray(response.day?.buckets),
      ),
    onResponse: (response) => { data.value = response },
    onClear: () => { data.value = null },
  })

  return {
    data,
    isLoading,
    isInitialLoading,
    notFound,
    error,
  }
}
