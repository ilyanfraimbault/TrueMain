import { describeFetchError } from '~/utils/errors'

export interface UseErrorToastOptions {
  /** Toast title. Defaults to a generic "Something went wrong". */
  title?: string
}

/**
 * Surfaces a fetch error as a toast with a consistent, human-friendly message.
 *
 * Complements (rather than replaces) an inline error state: the toast is the
 * transient "it just failed" signal, the inline alert is the persistent
 * fallback that stays on screen. Both read from {@link describeFetchError} so
 * the wording always matches.
 *
 * Watches the error ref and fires once per new failure; a successful refetch
 * clears the ref to null, which is ignored.
 *
 * A toast is a client-side event by nature, so the watcher is registered on the
 * client only.
 */
export function useErrorToast(
  error: MaybeRefOrGetter<unknown>,
  options: UseErrorToastOptions = {},
) {
  const toast = useToast()

  // `import.meta.client` is what makes the `immediate` below safe, and it is
  // load-bearing rather than defensive. Every error ref wired to this composable
  // today comes from a `server: false` fetch, so it is null during SSR and an
  // immediate run is a no-op — but nothing enforces that, and nothing said it.
  // Point this at an SSR-enabled source (`buildSummaryFetch`, the leaderboard)
  // and an immediate watcher would fire *during SSR*, where `toast.add` pushes
  // into state that serialises into the Nuxt payload: the toast then pops up on
  // load, for every visitor served that render, with no action behind it. The
  // guard is a build-time constant, so on the server the watcher does not exist
  // at all — the property holds structurally, not by convention.
  if (import.meta.client) {
    watch(
      () => toValue(error),
      (value) => {
        if (!value) return
        toast.add({
          title: options.title ?? 'Something went wrong',
          description: describeFetchError(value),
          color: 'error',
          icon: 'i-lucide-circle-alert',
        })
      },
      // Fire on the initial value too: an error can already be present at mount
      // (a cached error key on SPA navigation). The `if (!value) return` guard
      // makes immediate evaluation a no-op when there's nothing to report.
      { immediate: true },
    )
  }
}
