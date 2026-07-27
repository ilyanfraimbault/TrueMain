/**
 * LRU cache bounded by total byte size rather than entry count. Used for
 * `/_ipx/**` responses (see server/handlers/ipx-cached.ts): that route is
 * public and unauthenticated, accepting arbitrary modifiers against any path
 * under the allow-listed CDN domains, some of which (CommunityDragon splash
 * art) run several MB — an entry-count cap would assume every response is
 * icon-sized, which nothing about the route guarantees.
 *
 * Kept dependency-free and framework-agnostic (no h3/ipx imports) so it can
 * be unit-tested directly.
 */

export interface BoundedByteCacheOptions {
  /** Total bytes the cache may hold across all entries. */
  maxBytes: number
  /** A single entry larger than this is never stored, only passed through. */
  maxEntryBytes: number
}

export interface BoundedByteCache<T extends { byteLength: number }> {
  get: (key: string) => T | undefined
  /** No-op if `value.byteLength > maxEntryBytes`; otherwise evicts LRU entries until it fits. */
  set: (key: string, value: T) => void
  readonly size: number
  readonly bytes: number
}

export function createBoundedByteCache<T extends { byteLength: number }>(
  options: BoundedByteCacheOptions,
): BoundedByteCache<T> {
  const { maxBytes, maxEntryBytes } = options
  const store = new Map<string, T>()
  let bytes = 0

  function evict(key: string) {
    const entry = store.get(key)
    if (!entry) return
    store.delete(key)
    bytes -= entry.byteLength
  }

  return {
    get(key) {
      const entry = store.get(key)
      if (!entry) return undefined
      // Re-insert so the most recently served entries are the last evicted —
      // Map iterates in insertion order, so this is what makes it LRU.
      store.delete(key)
      store.set(key, entry)
      return entry
    },
    set(key, value) {
      if (value.byteLength > maxEntryBytes) return
      // Drop any existing entry for this key first so its bytes aren't
      // double-counted against the budget below.
      evict(key)
      while (store.size > 0 && bytes + value.byteLength > maxBytes) {
        const oldest = store.keys().next()
        if (oldest.done) break
        evict(oldest.value)
      }
      store.set(key, value)
      bytes += value.byteLength
    },
    get size() {
      return store.size
    },
    get bytes() {
      return bytes
    },
  }
}
