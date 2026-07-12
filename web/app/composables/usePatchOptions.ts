/**
 * Select items for the patch pickers: the most recent DDragon minors (deduped
 * to `major.minor`, capped at 12) plus any pinned patches — typically the
 * patch the API actually returned and the URL filter — so the picker never
 * shows blank for a value outside the recent list. Sorted newest first.
 */
export function usePatchOptions(
  versions: MaybeRefOrGetter<string[] | null | undefined>,
  ...pinned: MaybeRefOrGetter<string | null | undefined>[]
) {
  return computed(() => {
    const seen = new Set<string>(
      (toValue(versions) ?? [])
        .map(p => p.split('.').slice(0, 2).join('.'))
        .filter(Boolean)
        .slice(0, 12),
    )
    for (const value of pinned) {
      const patch = toValue(value)
      if (patch) seen.add(patch)
    }
    return [...seen]
      .map(p => ({ label: p, value: p }))
      .sort((a, b) => b.value.localeCompare(a.value, undefined, { numeric: true }))
  })
}
