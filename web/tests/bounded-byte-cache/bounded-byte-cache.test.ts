import { describe, expect, it } from 'vitest'
import { createBoundedByteCache } from '~~/server/utils/bounded-byte-cache'

interface Blob { byteLength: number }
const blob = (bytes: number): Blob => ({ byteLength: bytes })

describe('createBoundedByteCache', () => {
  it('returns undefined for a key that was never set', () => {
    const cache = createBoundedByteCache<Blob>({ maxBytes: 100, maxEntryBytes: 100 })
    expect(cache.get('a')).toBeUndefined()
  })

  it('returns what was stored and tracks size', () => {
    const cache = createBoundedByteCache<Blob>({ maxBytes: 100, maxEntryBytes: 100 })
    const entry = blob(10)
    cache.set('a', entry)
    expect(cache.get('a')).toBe(entry)
    expect(cache.size).toBe(1)
    expect(cache.bytes).toBe(10)
  })

  it('never stores an entry larger than maxEntryBytes', () => {
    const cache = createBoundedByteCache<Blob>({ maxBytes: 1000, maxEntryBytes: 50 })
    cache.set('huge', blob(51))
    expect(cache.get('huge')).toBeUndefined()
    expect(cache.size).toBe(0)
    expect(cache.bytes).toBe(0)
  })

  it('evicts the oldest entry once total bytes would exceed maxBytes', () => {
    const cache = createBoundedByteCache<Blob>({ maxBytes: 30, maxEntryBytes: 30 })
    cache.set('a', blob(10))
    cache.set('b', blob(10))
    cache.set('c', blob(15)) // 10+10+15 = 35 > 30, must evict to fit
    expect(cache.get('a')).toBeUndefined()
    expect(cache.get('b')).toBeDefined()
    expect(cache.get('c')).toBeDefined()
    expect(cache.bytes).toBe(25)
  })

  it('evicts multiple entries if one large entry needs the room', () => {
    const cache = createBoundedByteCache<Blob>({ maxBytes: 30, maxEntryBytes: 30 })
    cache.set('a', blob(10))
    cache.set('b', blob(10))
    cache.set('c', blob(30)) // must evict both a and b to fit
    expect(cache.get('a')).toBeUndefined()
    expect(cache.get('b')).toBeUndefined()
    expect(cache.get('c')).toBeDefined()
    expect(cache.bytes).toBe(30)
  })

  it('treats a get as a touch, protecting the entry from the next eviction', () => {
    const cache = createBoundedByteCache<Blob>({ maxBytes: 20, maxEntryBytes: 20 })
    cache.set('a', blob(10))
    cache.set('b', blob(10))
    cache.get('a') // 'a' is now the most recently used
    cache.set('c', blob(10)) // must evict exactly one of a/b to fit; 'b' is older
    expect(cache.get('a')).toBeDefined()
    expect(cache.get('b')).toBeUndefined()
    expect(cache.get('c')).toBeDefined()
  })

  it('re-setting an existing key does not double-count its bytes', () => {
    const cache = createBoundedByteCache<Blob>({ maxBytes: 100, maxEntryBytes: 100 })
    cache.set('a', blob(10))
    cache.set('a', blob(20))
    expect(cache.bytes).toBe(20)
    expect(cache.size).toBe(1)
  })
})
