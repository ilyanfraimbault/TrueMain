/**
 * The toast half of the error vocabulary.
 *
 * A toast reports the outcome of an action the user just took whose result
 * would otherwise leave no trace on screen — a link copied, a form submitted, a
 * batch finished. It never reports a page or a panel failing to load: that is
 * `FetchErrorAlert`'s job, and an alert stays on screen where a toast does not.
 * Nothing shows both for one event.
 *
 * The colour/icon pairs live here rather than at each `toast.add` so the
 * vocabulary cannot drift one call site at a time — which is exactly how the
 * app ended up with three icons for the same "this failed" (#1661).
 */
export function useActionToast() {
  const toast = useToast()

  return {
    /** The action did what it said. */
    success(title: string, description?: string) {
      toast.add({ title, description, color: 'success', icon: 'i-lucide-circle-check' })
    },
    /** The action ran but not cleanly — a partial result the user should look at. */
    warning(title: string, description?: string) {
      toast.add({ title, description, color: 'warning', icon: 'i-lucide-triangle-alert' })
    },
    /** The action did not go through. */
    failure(title: string, description?: string) {
      toast.add({ title, description, color: 'error', icon: 'i-lucide-triangle-alert' })
    },
  }
}
