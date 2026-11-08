import type { ComputedRef, MaybeRefOrGetter, Ref } from 'vue'
// Explicit imports rather than Nuxt auto-imports: this composable is unit tested
// outside the Nuxt context (`tests/last-good-payload`).
import { computed, shallowRef, toValue, watch } from 'vue'

/**
 * The payload a panel should draw: the current `data`, or — when a re-fetch failed and
 * Nuxt reset `data` to its default — the last successful payload fetched for the SAME
 * `key` (#1426). A payload fetched for another key (another window, another filter) is
 * never substituted: it would label one window's figures as another's.
 *
 * `null` means there is nothing measured to show, and the caller renders no figures at
 * all rather than defaulting every metric to zero.
 */
export function useLastGoodPayload<T>(
  data: Ref<T | null | undefined>,
  key: MaybeRefOrGetter<string>,
): ComputedRef<T | null> {
  const lastGood = shallowRef<{ key: string, value: T } | null>(null)

  watch(data, (value) => {
    if (value !== null && value !== undefined) {
      lastGood.value = { key: toValue(key), value }
    }
  }, { immediate: true })

  return computed(() => {
    const current = data.value
    if (current !== null && current !== undefined) {
      return current
    }
    const last = lastGood.value
    return last && last.key === toValue(key) ? last.value : null
  })
}

/**
 * What a panel fed by `useLastGoodPayload` draws (#2471), one value for the four
 * cases its `data`/`pending`/`error` trio can describe:
 * - `loading`: nothing to show yet, a request on its way — a skeleton;
 * - `empty`: the answer came and holds nothing;
 * - `ready`: data to draw;
 * - `stale`: data kept from an earlier answer while the latest refresh failed —
 *   drawn with a note saying so, never blanked by the alert;
 * - `failed`: the request failed and nothing was ever kept — the alert alone.
 */
export type PanelPhase = 'loading' | 'empty' | 'ready' | 'stale' | 'failed'

export function panelPhase(input: { hasData: boolean, pending: boolean, error: unknown }): PanelPhase {
  if (input.hasData) {
    return input.error ? 'stale' : 'ready'
  }
  if (input.error) {
    return 'failed'
  }
  return input.pending ? 'loading' : 'empty'
}
