import type { ProfileResponse } from '~~/shared/types/profile'

/**
 * Single-shot fetch of a truemain profile for the given <c>nameTag</c>.
 * Mirrors the contract of <c>useTruemainMatches</c> — 404 from the API is
 * surfaced as <c>notFound = true</c> so the page can render an empty state
 * instead of an error.
 *
 * Client-only by design (private-ish, no SSR cross-pollination between
 * viewers).
 */
export function useTruemainProfile(nameTag: MaybeRefOrGetter<string>) {
  const nameTagRef = computed(() => toValue(nameTag))

  const data = ref<ProfileResponse | null>(null)
  const isLoading = ref(false)
  const isInitialLoading = ref(true)
  const notFound = ref(false)
  const error = ref<unknown>(null)

  async function fetchProfile() {
    if (!nameTagRef.value) {
      data.value = null
      notFound.value = false
      isInitialLoading.value = false
      return
    }

    isLoading.value = true
    error.value = null
    try {
      const response = await $fetch<ProfileResponse | null>(
        `/api/truemains/${encodeURIComponent(nameTagRef.value)}/profile`,
        { ignoreResponseError: true },
      )

      // `ignoreResponseError: true` turns the controller's 404 into a null
      // body — the only way to tell "not found" apart from "no body" is the
      // shape check on identity. Anything missing the identity object we
      // treat as a 404.
      if (!response || typeof response !== 'object' || !response.identity) {
        notFound.value = true
        data.value = null
        return
      }

      notFound.value = false
      data.value = response
    }
    catch (err) {
      error.value = err
    }
    finally {
      isLoading.value = false
      isInitialLoading.value = false
    }
  }

  async function refresh() {
    data.value = null
    notFound.value = false
    isInitialLoading.value = true
    await fetchProfile()
  }

  // Refetch when the nameTag changes (e.g. user navigates between profiles)
  // and on first mount. Empty nameTag short-circuits inside fetchProfile so
  // a parent page can bind to a still-empty ref without triggering a 404
  // round trip.
  watch(nameTagRef, () => { void fetchProfile() }, { immediate: true })

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
