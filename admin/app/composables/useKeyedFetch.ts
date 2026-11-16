import type { MaybeRefOrGetter, Ref } from 'vue'
import type { KeyedFetch, SameKey } from '~/utils/keyed-fetch'
// Explicit imports rather than Nuxt auto-imports: unit tested outside the Nuxt
// context (`tests/keyed-fetch`).
import { computed, getCurrentScope, onScopeDispose, shallowRef, toValue, watch } from 'vue'
import { failKeyed, keyedId, keyedValue, startKeyed } from '~/utils/keyed-fetch'

interface KeyedFetchOptions<K> {
  /**
   * The entity to follow: each new value loads it, `null` goes back to `idle`.
   * Leave it out to drive the fetch with `load(id)` only.
   */
  id?: MaybeRefOrGetter<K | null>
  /** How two ids compare; `Object.is` by default. */
  same?: SameKey<K>
}

/**
 * One fetch for one entity at a time (#2471), as a `KeyedFetch` union: only the
 * answer of the latest request is written (the `usePaletteSearch` token), so a slow
 * answer for A can never land while B is open, nor clear B's `loading`. A reload of
 * the same id keeps its answer on screen; asking for another id drops it.
 */
export function useKeyedFetch<K, T>(fetcher: (id: K) => Promise<T>, options: KeyedFetchOptions<K> = {}) {
  const same = options.same ?? Object.is
  const state = shallowRef<KeyedFetch<K, T>>({ s: 'idle' }) as Ref<KeyedFetch<K, T>>
  let latest = 0

  async function load(id: K): Promise<void> {
    const request = ++latest
    state.value = startKeyed(state.value, id, same)
    try {
      const value = await fetcher(id)
      if (request === latest) {
        state.value = { s: 'ready', id, value }
      }
    }
    catch (error) {
      if (request === latest) {
        state.value = failKeyed(state.value, id, error, same)
      }
    }
  }

  /** Reloads the entity on screen, keeping its answer meanwhile; a no-op when idle. */
  function reload(): Promise<void> {
    const id = keyedId(state.value)
    return id === null ? Promise.resolve() : load(id)
  }

  /** Back to `idle`; an answer still in flight is dropped. */
  function reset() {
    latest++
    state.value = { s: 'idle' }
  }

  if (options.id !== undefined) {
    const source = options.id
    watch(() => toValue(source), (id) => {
      if (id === null) {
        reset()
      }
      else {
        void load(id)
      }
    }, { immediate: true })
  }

  // Nothing lands after the owner is gone.
  if (getCurrentScope()) {
    onScopeDispose(() => {
      latest++
    })
  }

  return {
    state,
    /** The answer to draw: the current one, or the same-id one a reload or a failure kept. */
    value: computed(() => keyedValue(state.value)),
    load,
    reload,
    reset,
  }
}
