import type { ProfileResponse } from '~~/shared/types/profile'

/**
 * Single-shot fetch of a truemain profile for the given <c>nameTag</c>.
 * Mirrors the contract of <c>useTruemainMatches</c> — 404 from the API is
 * surfaced as <c>notFound = true</c> so the page can render an empty state
 * instead of an error.
 *
 * Client-only by design (private-ish, no SSR cross-pollination between
 * viewers) — see {@link useTruemainFetch} for the shared lifecycle.
 */
export function useTruemainProfile(nameTag: MaybeRefOrGetter<string>) {
  const data = ref<ProfileResponse | null>(null)

  const { isLoading, isInitialLoading, notFound, error, execute } = useTruemainFetch<ProfileResponse>(nameTag, {
    request: tag => $fetch<ProfileResponse | null>(
      `/api/truemains/${encodeURIComponent(tag)}/profile`,
      { ignoreResponseError: true },
    ),
    // Anything missing the identity object we treat as a 404.
    validate: (response): response is ProfileResponse =>
      Boolean(response && typeof response === 'object' && response.identity),
    onResponse: (response) => { data.value = response },
    onClear: () => { data.value = null },
  })

  async function refresh() {
    data.value = null
    notFound.value = false
    isInitialLoading.value = true
    await execute()
  }

  // Intentionally exposing raw refs (not `readonly()` wrappers) — readonly
  // proxies cascade DeepReadonly across the entire response shape, which
  // then collides with the consuming components' mutable prop types
  // (`ProfileMainChampion[]` vs. `readonly ProfileMainChampion[]`).
  return {
    data,
    isLoading,
    isInitialLoading,
    notFound,
    error,
    refresh,
  }
}
