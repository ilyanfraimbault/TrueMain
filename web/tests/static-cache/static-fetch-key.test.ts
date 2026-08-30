import { describe, expect, it } from 'vitest'
import { RUNE_TREE_KEY_PREFIX, staticFetchKey } from '~/composables/useBuildAssets'

describe('staticFetchKey', () => {
  it('suffixes the prefix with the resolved patch', () => {
    expect(staticFetchKey('static-items', '16.15.1')).toBe('static-items-16.15.1')
  })

  it('falls back to `latest` while the patch is unresolved', () => {
    expect(staticFetchKey(RUNE_TREE_KEY_PREFIX, null)).toBe('rune-tree-latest')
    expect(staticFetchKey(RUNE_TREE_KEY_PREFIX, undefined)).toBe('rune-tree-latest')
    expect(staticFetchKey(RUNE_TREE_KEY_PREFIX, '')).toBe('rune-tree-latest')
  })

  it('honours a caller-supplied unresolved segment', () => {
    expect(staticFetchKey('static-items', null, 'deferred')).toBe('static-items-deferred')
  })

  it('is the key the prefetch plugin has to warm for the rune tree', () => {
    // #1231: the plugin wrote a bare `rune-tree` while the composable read
    // `rune-tree-latest`, so the warm-up was dead and every page refetched the
    // tree. Both now build the key here — this asserts the value that fixed it.
    expect(staticFetchKey(RUNE_TREE_KEY_PREFIX)).toBe('rune-tree-latest')
  })
})
