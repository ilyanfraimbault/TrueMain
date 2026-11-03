import type { MaybeRefOrGetter } from 'vue'
import type { OpsCache } from './ops-cache'
// Explicit imports rather than Nuxt auto-imports: this module is unit tested
// outside the Nuxt context (`tests/ops-cache`).
import { onScopeDispose, toValue, watch } from 'vue'
import { opsCache, opsCacheShown } from './ops-cache'

/** The slice of ofetch's response context `opsCacheFetchOptions` reads. */
export interface OpsResponseContext {
  response: { ok: boolean, _data?: unknown }
}

/**
 * The `useFetch` options that plug one ops read into the cache (#2306):
 *
 * - `default` draws the stored payload while the request runs — and again when it fails,
 *   since Nuxt resets a failed fetch's `data` to `default()`; that is what keeps the
 *   previous content on screen under the page's single stale notice (#1668).
 * - `getCachedData` skips the request altogether when the stored payload is only a few
 *   seconds old (`OPS_CACHE_FRESH_MS`), on the initial fetch only: a manual refresh, a
 *   live-refresh tick (#1411) or a filter change on a key already mounted goes to the
 *   network.
 * - `onRequest` notes the key the request was sent for, and `onResponse` stores a
 *   successful payload under that key — not under the live key, which may already have
 *   moved to another filter by the time a slow response lands. (ofetch hands both hooks
 *   the same context object; the request's query cannot be read back from it reliably.)
 *
 * `canStore` is the session gate: a response landing after logout is not kept.
 */
export function opsCacheFetchOptions<T = unknown>(
  key: MaybeRefOrGetter<string>,
  canStore: () => boolean = () => true,
  cache: OpsCache = opsCache,
  shown: Map<string, number> = opsCacheShown,
) {
  const requestKeys = new WeakMap<object, string>()
  return {
    default: () => cache.get(toValue(key))?.value as T | undefined,
    getCachedData: (cacheKey: string, _nuxtApp: unknown, ctx: { cause: string }) =>
      ctx.cause === 'initial' ? cache.fresh(cacheKey)?.value as T | undefined : undefined,
    onRequest: (context: object) => {
      requestKeys.set(context, toValue(key))
    },
    onResponse: (context: OpsResponseContext) => {
      const requestKey = requestKeys.get(context)
      if (requestKey === undefined || !context.response.ok || !canStore()) {
        return
      }
      cache.set(requestKey, context.response._data)
      shown.delete(requestKey)
    },
  }
}

/**
 * Register the key's payload in `opsCacheShown` while it is drawn from the cache and its
 * revalidation has not landed — but not when the payload was fresh enough to skip the
 * request (`OPS_CACHE_FRESH_MS`): that one is as current as a payload just fetched.
 * Unregistered when the key moves on or the panel unmounts, so a page left behind never
 * labels the next one.
 */
export function trackOpsCacheShown(
  key: MaybeRefOrGetter<string>,
  cache: OpsCache = opsCache,
  shown: Map<string, number> = opsCacheShown,
): void {
  watch(() => toValue(key), (current, previous) => {
    if (previous !== undefined) {
      shown.delete(previous)
    }
    const entry = cache.get(current)
    if (entry && !cache.fresh(current)) {
      shown.set(current, entry.fetchedAt)
    }
  }, { immediate: true })
  onScopeDispose(() => {
    shown.delete(toValue(key))
  })
}
