import { watch } from 'vue'

/** The first non-empty string the route carries under `key`, or null. */
export function queryString(raw: unknown): string | null {
  const value = Array.isArray(raw) ? raw[0] : raw
  return typeof value === 'string' && value.trim() ? value : null
}

/**
 * Hands `apply` the value the route carries under `key` — at setup when
 * `immediate`, then each time it changes to a non-empty value. The ⌘K palette
 * (#2290) links into a page that may already be open: the component is reused,
 * setup does not re-run, and only the query moves, so reading it once on mount
 * misses the second jump.
 */
export function useQueryValue(key: string, apply: (value: string) => void | Promise<void>, options: { immediate?: boolean } = {}) {
  const route = useRoute()
  // An async `apply` is awaited, so a rejection reaches Vue's error handler.
  watch(() => queryString(route.query[key]), async (value) => {
    if (value !== null) {
      await apply(value)
    }
  }, { immediate: options.immediate ?? false })
}
