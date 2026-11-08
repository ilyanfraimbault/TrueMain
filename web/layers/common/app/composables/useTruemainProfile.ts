import type { ProfileResponse } from '#shared/types/profile'

interface UseTruemainProfileOptions {
  /** Hold the request back while false — see {@link useTruemainFetch}. */
  enabled?: MaybeRefOrGetter<boolean>
  /**
   * Fetch on the server and ride the SSR payload (#2359), so the profile's own
   * page renders its banner, ranked card and mains into the HTML a crawler gets.
   * A profile is public and keyed on the slug alone, so nothing viewer-specific
   * reaches shared markup, and the client hydrates from the same payload — the
   * server/client race of #862 needs a fetch the client does not see, which a
   * `useAsyncData` read is not.
   */
  ssr?: boolean
}

/**
 * Single-shot fetch of a truemain profile for the given <c>nameTag</c>.
 * Mirrors the contract of <c>useTruemainMatches</c> — a 404 from the API is
 * surfaced as <c>notFound = true</c> so the page can render an empty state,
 * any other failure as <c>error</c> (#2476).
 *
 * Client-only by default — see {@link useTruemainFetch} for the shared
 * lifecycle — or server-rendered with `ssr` (see the option).
 */
export function useTruemainProfile(
  nameTag: MaybeRefOrGetter<string>,
  options: UseTruemainProfileOptions = {},
) {
  if (options.ssr) return useServerRenderedProfile(nameTag)

  const data = ref<ProfileResponse | null>(null)
  const apiFetch = useApiFetch()

  const { isLoading, isInitialLoading, notFound, error, reload, ready } = useTruemainFetch<ProfileResponse>(nameTag, {
    enabled: options.enabled,
    request: (tag, signal) => apiFetch<ProfileResponse | null>(
      `/truemains/${encodeURIComponent(tag)}/profile`,
      { signal },
    ),
    // A 2xx missing the identity object reads as a 404.
    validate: (response): response is ProfileResponse =>
      Boolean(response && typeof response === 'object' && response.identity),
    onResponse: (response) => { data.value = response },
    onClear: () => { data.value = null },
  })

  async function refresh() {
    data.value = null
    await reload()
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
    ready,
    refresh,
  }
}

function isProfile(response: unknown): response is ProfileResponse {
  return Boolean(response && typeof response === 'object' && (response as ProfileResponse).identity)
}

/**
 * The `ssr` flavour: one `useAsyncData` read, with the client-only flavour's
 * return shape. Only a definite 404 is "not found"; any other failure is an
 * error, so an API outage never renders as an unknown player.
 */
function useServerRenderedProfile(nameTag: MaybeRefOrGetter<string>) {
  const nameTagRef = computed(() => toValue(nameTag))
  const apiFetch = useApiFetch()

  const result = useAsyncData<ProfileResponse | null>(
    () => `truemain-profile-${nameTagRef.value}`,
    async (_nuxtApp, { signal }) => {
      if (!nameTagRef.value) return null
      try {
        const response = await apiFetch<ProfileResponse | null>(
          `/truemains/${encodeURIComponent(nameTagRef.value)}/profile`,
          { signal },
        )
        return isProfile(response) ? response : null
      }
      catch (err) {
        if (fetchErrorStatus(err) === 404) return null
        throw err
      }
    },
    { watch: [nameTagRef], default: () => null },
  )

  const { data, status, error } = result
  const isLoading = computed(() => status.value === 'pending')
  const isInitialLoading = computed(() => (status.value === 'pending' || status.value === 'idle') && !data.value)
  const notFound = computed(() => status.value === 'success' && !data.value && Boolean(nameTagRef.value))

  async function refresh() {
    await result.refresh()
  }

  return {
    data,
    isLoading,
    isInitialLoading,
    notFound,
    error,
    ready: result.then(() => undefined),
    refresh,
  }
}
