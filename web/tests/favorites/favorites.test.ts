import type { FavoriteTruemain } from '~~/app/utils/favorites'
import { beforeEach, describe, expect, it } from 'vitest'
import { ref } from 'vue'
import {
  createFavoritesStore,
  FAVORITES_LIMIT,
  FAVORITES_STORAGE_KEY,
  favoriteKey,
  favoriteNameTag,
  normalizeFavorites,
  parseStoredFavorites,
  resolveFavoriteIdentity,
} from '~~/app/utils/favorites'

// The composable's behaviour lives in `createFavoritesStore` (the Nuxt wrapper
// `useFavoriteTruemains` only supplies the shared state ref and the mount-time
// storage read), so these drive the real store against happy-dom's
// localStorage — no mocks of the storage layer.

function makeStore(now: () => number = () => 1_000) {
  const state = ref<FavoriteTruemain[]>([])
  return { state, store: createFavoritesStore(state, now) }
}

function stored(): unknown {
  const raw = window.localStorage.getItem(FAVORITES_STORAGE_KEY)
  return raw === null ? null : JSON.parse(raw)
}

beforeEach(() => {
  window.localStorage.clear()
})

describe('favoriteNameTag', () => {
  it('joins the Riot ID with the app-wide `-` separator', () => {
    expect(favoriteNameTag('Sheiden', '1234')).toBe('Sheiden-1234')
  })

  it('falls back to the bare game name when there is no tag line', () => {
    expect(favoriteNameTag('Sheiden', null)).toBe('Sheiden')
    expect(favoriteNameTag(' Sheiden ', '  ')).toBe('Sheiden')
  })
})

describe('favoriteKey', () => {
  it('matches Riot IDs case-insensitively, like the backend lookup', () => {
    expect(favoriteKey(' SHEIDEN-1234 ')).toBe(favoriteKey('sheiden-1234'))
  })
})

describe('resolveFavoriteIdentity', () => {
  it('resolves and trims a usable Riot identity', () => {
    expect(resolveFavoriteIdentity({ gameName: ' Sheiden ', tagLine: ' 1234 ' })).toEqual({
      gameName: 'Sheiden',
      tagLine: '1234',
      nameTag: 'Sheiden-1234',
    })
  })

  it('accepts an untagged game name', () => {
    expect(resolveFavoriteIdentity({ gameName: 'Sheiden', tagLine: null })).toEqual({
      gameName: 'Sheiden',
      tagLine: null,
      nameTag: 'Sheiden',
    })
  })

  it('rejects an empty game name, exactly like the read path (#872)', () => {
    // The trap: with a tag line, an empty game name still produces a non-empty
    // `-1234` slug, so a nameTag-only guard would let it through — and
    // `normalizeFavorite` would then drop the stored row on the next read.
    expect(favoriteNameTag('', '1234')).toBe('-1234')
    expect(resolveFavoriteIdentity({ gameName: '', tagLine: '1234' })).toBeNull()
    expect(resolveFavoriteIdentity({ gameName: '   ', tagLine: '1234' })).toBeNull()
    expect(resolveFavoriteIdentity({ gameName: '', tagLine: null })).toBeNull()
  })
})

describe('parseStoredFavorites', () => {
  it('returns an empty list when nothing is stored', () => {
    expect(parseStoredFavorites(null)).toEqual([])
    expect(parseStoredFavorites('')).toEqual([])
  })

  it('tolerates corrupted JSON instead of throwing', () => {
    expect(parseStoredFavorites('{not json')).toEqual([])
  })

  it('ignores a payload that is not an array', () => {
    expect(parseStoredFavorites('{"nameTag":"Sheiden-1234"}')).toEqual([])
  })

  it('drops entries with no usable game name', () => {
    const raw = JSON.stringify([{ gameName: '' }, null, 42, { tagLine: '1234' }])
    expect(parseStoredFavorites(raw)).toEqual([])
  })

  it('rebuilds a missing nameTag and defaults the optional fields', () => {
    const raw = JSON.stringify([{ gameName: 'Sheiden', tagLine: '1234', region: 'atlantis' }])
    expect(parseStoredFavorites(raw)).toEqual([{
      nameTag: 'Sheiden-1234',
      gameName: 'Sheiden',
      tagLine: '1234',
      region: null,
      profileIconId: null,
      addedAt: 0,
    }])
  })

  it('keeps a known region slug', () => {
    const raw = JSON.stringify([{ gameName: 'Sheiden', tagLine: '1234', region: 'korea', profileIconId: 4021 }])
    expect(parseStoredFavorites(raw)[0]).toMatchObject({ region: 'korea', profileIconId: 4021 })
  })
})

describe('normalizeFavorites', () => {
  it('orders newest first', () => {
    const entries = normalizeFavorites([
      { gameName: 'Older', tagLine: 'A', addedAt: 1 },
      { gameName: 'Newer', tagLine: 'B', addedAt: 9 },
    ])
    expect(entries.map(e => e.gameName)).toEqual(['Newer', 'Older'])
  })

  it('de-duplicates on the case-insensitive identity key', () => {
    const entries = normalizeFavorites([
      { gameName: 'Sheiden', tagLine: '1234', addedAt: 2 },
      { gameName: 'SHEIDEN', tagLine: '1234', addedAt: 1 },
    ])
    expect(entries).toHaveLength(1)
    expect(entries[0]!.gameName).toBe('Sheiden')
  })

  it('collapses duplicates onto the newest record, whatever the input order', () => {
    // Hand-edited storage can list the stale copy first; position must not
    // decide which one survives.
    const entries = normalizeFavorites([
      { gameName: 'Sheiden', tagLine: '1234', profileIconId: 1, addedAt: 1 },
      { gameName: 'Sheiden', tagLine: '1234', profileIconId: 2, addedAt: 9 },
    ])
    expect(entries).toHaveLength(1)
    expect(entries[0]).toMatchObject({ profileIconId: 2, addedAt: 9 })
  })

  it('caps the list at the storage limit', () => {
    const entries = normalizeFavorites(
      Array.from({ length: FAVORITES_LIMIT + 5 }, (_, i) => ({ gameName: `P${i}`, tagLine: '1', addedAt: i })),
    )
    expect(entries).toHaveLength(FAVORITES_LIMIT)
    // The oldest overflow entries are the ones dropped.
    expect(entries.at(-1)!.gameName).toBe('P5')
  })
})

describe('favorites store', () => {
  it('starts empty — the SSR/hydration render never depends on storage', () => {
    window.localStorage.setItem(
      FAVORITES_STORAGE_KEY,
      JSON.stringify([{ gameName: 'Sheiden', tagLine: '1234', addedAt: 5 }]),
    )
    const { store } = makeStore()
    // Constructing the store must not read storage: that read is what would
    // desync the client's first render from the server HTML (#838/#840).
    expect(store.favorites.value).toEqual([])
    expect(store.isFavorite('Sheiden-1234')).toBe(false)
  })

  it('fills the list from storage only when explicitly loaded', () => {
    window.localStorage.setItem(
      FAVORITES_STORAGE_KEY,
      JSON.stringify([{ gameName: 'Sheiden', tagLine: '1234', addedAt: 5 }]),
    )
    const { store } = makeStore()
    store.loadFromStorage()
    expect(store.favorites.value).toHaveLength(1)
    expect(store.isFavorite('sheiden-1234')).toBe(true)
  })

  it('adds an entry and persists it', () => {
    const { store } = makeStore(() => 1_700)
    store.add({ gameName: 'Sheiden', tagLine: '1234', region: 'europe', profileIconId: 4021 })

    expect(store.favorites.value).toEqual([{
      nameTag: 'Sheiden-1234',
      gameName: 'Sheiden',
      tagLine: '1234',
      region: 'europe',
      profileIconId: 4021,
      addedAt: 1_700,
    }])
    expect(stored()).toEqual(store.favorites.value)
    expect(store.count.value).toBe(1)
  })

  it('ignores a duplicate add', () => {
    const { store } = makeStore()
    store.add({ gameName: 'Sheiden', tagLine: '1234' })
    store.add({ gameName: 'SHEIDEN', tagLine: '1234' })
    expect(store.favorites.value).toHaveLength(1)
  })

  it('refuses an entry the read path would discard (#872)', () => {
    const { store } = makeStore()
    // Write and read now share one notion of a valid entry: an empty game name
    // is refused here rather than stored as a `-1234` slug that
    // `normalizeFavorite` throws away on the next load.
    store.add({ gameName: '', tagLine: '1234' })
    store.add({ gameName: '   ', tagLine: '1234', region: 'europe' })

    expect(store.favorites.value).toEqual([])
    expect(stored()).toBeNull()
    expect(store.isFavorite('-1234')).toBe(false)
  })

  it('reports a refused toggle as not-followed instead of claiming the follow', () => {
    const { store } = makeStore()
    expect(store.toggle({ gameName: '', tagLine: '1234' })).toBe(false)
    expect(store.favorites.value).toEqual([])
  })

  it('leaves pre-existing entries alone when a later write happens', () => {
    // The tightened guard is a write-path check, not a new validity rule: a
    // list already in storage keeps flowing through `normalizeFavorites`
    // untouched, so nobody loses the players they follow.
    const { store } = makeStore(() => 2_000)
    window.localStorage.setItem(
      FAVORITES_STORAGE_KEY,
      JSON.stringify([{ gameName: 'Older', tagLine: '1', addedAt: 5 }]),
    )
    store.loadFromStorage()

    store.add({ gameName: '', tagLine: '1234' })
    expect(store.favorites.value.map(e => e.nameTag)).toEqual(['Older-1'])

    store.add({ gameName: 'Newer', tagLine: '2' })
    expect(store.favorites.value.map(e => e.nameTag)).toEqual(['Newer-2', 'Older-1'])
  })

  it('keeps the most recently added entry first', () => {
    let clock = 1_000
    const { store } = makeStore(() => ++clock)
    store.add({ gameName: 'First', tagLine: '1' })
    store.add({ gameName: 'Second', tagLine: '2' })
    expect(store.favorites.value.map(e => e.gameName)).toEqual(['Second', 'First'])
  })

  it('removes an entry case-insensitively and persists the removal', () => {
    const { store } = makeStore()
    store.add({ gameName: 'Sheiden', tagLine: '1234' })
    store.remove('sheiden-1234')
    expect(store.favorites.value).toEqual([])
    expect(stored()).toEqual([])
  })

  it('toggles both ways and reports the new state', () => {
    const { store } = makeStore()
    expect(store.toggle({ gameName: 'Sheiden', tagLine: '1234' })).toBe(true)
    expect(store.isFavorite('Sheiden-1234')).toBe(true)
    expect(store.toggle({ gameName: 'Sheiden', tagLine: '1234' })).toBe(false)
    expect(store.isFavorite('Sheiden-1234')).toBe(false)
  })

  it('handles untagged Riot IDs', () => {
    const { store } = makeStore()
    store.add({ gameName: 'Sheiden', tagLine: null })
    expect(store.favorites.value[0]!.nameTag).toBe('Sheiden')
    expect(store.isFavorite('Sheiden')).toBe(true)
  })

  it('clears everything', () => {
    const { store } = makeStore()
    store.add({ gameName: 'Sheiden', tagLine: '1234' })
    store.clear()
    expect(store.favorites.value).toEqual([])
    expect(stored()).toEqual([])
  })

  it('flags the cap so the UI can decline the click', () => {
    let clock = 0
    const { store } = makeStore(() => ++clock)
    for (let i = 0; i < FAVORITES_LIMIT; i++) {
      store.add({ gameName: `P${i}`, tagLine: '1' })
    }
    expect(store.atLimit.value).toBe(true)
    expect(store.favorites.value).toHaveLength(FAVORITES_LIMIT)
  })

  it('evicts the oldest rather than refusing when added past the cap', () => {
    // `add` is deliberately eviction-based: the cap lives in one place
    // (normalizeFavorites) so reads and writes obey the same rule. The refusal
    // is a UI affordance on top (see FavoriteToggle), not a store guarantee.
    let clock = 0
    const { store } = makeStore(() => ++clock)
    for (let i = 0; i < FAVORITES_LIMIT; i++) {
      store.add({ gameName: `P${i}`, tagLine: '1' })
    }

    store.add({ gameName: 'Overflow', tagLine: '1' })
    expect(store.favorites.value).toHaveLength(FAVORITES_LIMIT)
    expect(store.favorites.value[0]!.gameName).toBe('Overflow')
    // P0 was the oldest, so it is the one that fell off.
    expect(store.isFavorite('P0-1')).toBe(false)
    expect(store.isFavorite('P1-1')).toBe(true)
  })

  it('re-reads storage on demand, so a write from another tab is picked up', () => {
    const { store } = makeStore()
    store.add({ gameName: 'Sheiden', tagLine: '1234' })

    window.localStorage.setItem(
      FAVORITES_STORAGE_KEY,
      JSON.stringify([{ gameName: 'Other', tagLine: '9999', addedAt: 3 }]),
    )
    store.loadFromStorage()

    expect(store.favorites.value.map(e => e.gameName)).toEqual(['Other'])
  })

  it('drops corrupted storage rather than surfacing garbage', () => {
    window.localStorage.setItem(FAVORITES_STORAGE_KEY, 'not json at all')
    const { store } = makeStore()
    store.loadFromStorage()
    expect(store.favorites.value).toEqual([])
  })
})
