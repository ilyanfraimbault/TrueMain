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
 */
export function useErrorToast(
  error: MaybeRefOrGetter<unknown>,
  options: UseErrorToastOptions = {},
) {
  const toast = useToast()

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
  )
}
