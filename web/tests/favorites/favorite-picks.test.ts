import type { FavoritePick } from '#common/utils/favorites'
import { beforeEach, describe, expect, it } from 'vitest'
import { ref } from 'vue'
import {
  createFavoritePicksStore,
  FAVORITE_PICKS_LIMIT_PER_KIND,
  FAVORITE_PICKS_STORAGE_KEY,
  FAVORITES_STORAGE_KEY,
  favoritePickQuery,
  normalizeFavoritePicks,
  parseStoredFavoritePicks,
  resolveFavoritePick,
} from '#common/utils/favorites'

// Starred champions and matchups (#2080): their own key next to the player
// list, the same untrusted-storage rules, a cap per kind.

function makeStore(now: () => number = () => 1_000) {
  const state = ref<FavoritePick[]>([])
  return { state, store: createFavoritePicksStore(state, now) }
}

const ahriMid = { kind: 'champion', championId: 103, position: 'MIDDLE' } as const
const ahriVsZed = { kind: 'matchup', championId: 103, opponentChampionId: 238, position: 'MIDDLE' } as const

beforeEach(() => {
  window.localStorage.clear()
})

describe('resolveFavoritePick', () => {
  it('keeps a champion with or without a position', () => {
    expect(resolveFavoritePick(ahriMid)).toEqual(ahriMid)
    expect(resolveFavoritePick({ kind: 'champion', championId: 103, position: 'NOWHERE' }))
      .toEqual({ kind: 'champion', championId: 103, position: null })
  })

  it('needs a whole matchup: an opponent other than the champion, and a position', () => {
    expect(resolveFavoritePick(ahriVsZed)).toEqual(ahriVsZed)
    expect(resolveFavoritePick({ ...ahriVsZed, opponentChampionId: 103 })).toBeNull()
    expect(resolveFavoritePick({ ...ahriVsZed, position: null })).toBeNull()
    expect(resolveFavoritePick({ ...ahriVsZed, opponentChampionId: 'Zed' })).toBeNull()
  })

  it('refuses anything else', () => {
    for (const value of [null, 'Ahri', 103, { kind: 'build', championId: 103 }, { kind: 'champion', championId: -1 }])
      expect(resolveFavoritePick(value)).toBeNull()
  })
})

describe('normalizeFavoritePicks', () => {
  it('orders newest first and keeps the most recent copy of a duplicate', () => {
    const picks = normalizeFavoritePicks([
      { ...ahriMid, addedAt: 1 },
      { ...ahriVsZed, addedAt: 3 },
      { ...ahriMid, addedAt: 5 },
    ])
    expect(picks).toEqual([{ ...ahriMid, addedAt: 5 }, { ...ahriVsZed, addedAt: 3 }])
  })

  it('treats a champion on two positions as two favorites', () => {
    const picks = normalizeFavoritePicks([{ ...ahriMid, addedAt: 1 }, { ...ahriMid, position: 'TOP', addedAt: 2 }])
    expect(picks).toHaveLength(2)
  })

  it('caps each kind on its own, dropping the oldest', () => {
    const champions = Array.from({ length: FAVORITE_PICKS_LIMIT_PER_KIND + 2 }, (_, i) =>
      ({ kind: 'champion', championId: i + 1, position: null, addedAt: i }))
    const picks = normalizeFavoritePicks([...champions, { ...ahriVsZed, addedAt: 0 }])
    expect(picks.filter(pick => pick.kind === 'champion')).toHaveLength(FAVORITE_PICKS_LIMIT_PER_KIND)
    expect(picks.some(pick => pick.kind === 'champion' && pick.championId === 1)).toBe(false)
    expect(picks.some(pick => pick.kind === 'matchup')).toBe(true)
  })
})

describe('parseStoredFavoritePicks', () => {
  it('degrades corruption to an empty list', () => {
    expect(parseStoredFavoritePicks(null)).toEqual([])
    expect(parseStoredFavoritePicks('{not json')).toEqual([])
    expect(parseStoredFavoritePicks('{"kind":"champion"}')).toEqual([])
  })

  it('drops unusable records and keeps the rest', () => {
    const raw = JSON.stringify([{ ...ahriMid, addedAt: 2 }, { kind: 'matchup', championId: 103 }])
    expect(parseStoredFavoritePicks(raw)).toEqual([{ ...ahriMid, addedAt: 2 }])
  })
})

describe('favoritePickQuery', () => {
  it('opens a champion on its position and a matchup on its triple', () => {
    expect(favoritePickQuery(ahriMid)).toEqual({ position: 'MIDDLE' })
    expect(favoritePickQuery({ ...ahriMid, position: null })).toEqual({})
    expect(favoritePickQuery(ahriVsZed)).toEqual({ champion: '103', position: 'MIDDLE', opponent: '238' })
  })
})

describe('favorite picks store', () => {
  it('starts empty and reads storage only when asked', () => {
    window.localStorage.setItem(FAVORITE_PICKS_STORAGE_KEY, JSON.stringify([{ ...ahriMid, addedAt: 1 }]))
    const { store } = makeStore()
    expect(store.picks.value).toEqual([])
    store.loadPicksFromStorage()
    expect(store.isFavoritePick(ahriMid)).toBe(true)
  })

  it('toggles a pick on and off, persisting each write under its own key', () => {
    const { store } = makeStore(() => 1_700)
    expect(store.togglePick(ahriVsZed)).toBe(true)
    expect(store.picks.value).toEqual([{ ...ahriVsZed, addedAt: 1_700 }])
    expect(JSON.parse(window.localStorage.getItem(FAVORITE_PICKS_STORAGE_KEY)!)).toEqual(store.picks.value)

    expect(store.togglePick(ahriVsZed)).toBe(false)
    expect(store.picks.value).toEqual([])
    expect(window.localStorage.getItem(FAVORITE_PICKS_STORAGE_KEY)).toBe('[]')
  })

  it('never touches the followed players', () => {
    const players = JSON.stringify([{ gameName: 'Sheiden', tagLine: '1234', addedAt: 5 }])
    window.localStorage.setItem(FAVORITES_STORAGE_KEY, players)
    const { store } = makeStore()
    store.togglePick(ahriMid)
    store.clearPicks()
    expect(window.localStorage.getItem(FAVORITES_STORAGE_KEY)).toBe(players)
  })

  it('refuses an unusable input instead of reporting a star that never landed', () => {
    const { store } = makeStore()
    expect(store.togglePick({ ...ahriVsZed, opponentChampionId: 103 })).toBe(false)
    expect(store.picks.value).toEqual([])
  })

  it('reports a kind as full at the cap', () => {
    const { store } = makeStore()
    for (let id = 1; id <= FAVORITE_PICKS_LIMIT_PER_KIND; id++)
      store.togglePick({ kind: 'champion', championId: id, position: null })
    expect(store.isPickKindFull('champion')).toBe(true)
    expect(store.isPickKindFull('matchup')).toBe(false)
  })
})
