import type { FavoriteChampion } from '#common/utils/favorites'
import { beforeEach, describe, expect, it } from 'vitest'
import { ref } from 'vue'
import {
  createFavoriteChampionsStore,
  FAVORITE_CHAMPIONS_LIMIT,
  FAVORITE_CHAMPIONS_STORAGE_KEY,
  FAVORITES_STORAGE_KEY,
  normalizeFavoriteChampions,
  parseStoredFavoriteChampions,
  resolveFavoriteChampion,
} from '#common/utils/favorites'

// Starred champions (#2080): their own key next to the player list, the same
// untrusted-storage rules, a cap.

function makeStore(now: () => number = () => 1_000) {
  const state = ref<FavoriteChampion[]>([])
  return { state, store: createFavoriteChampionsStore(state, now) }
}

const ahriMid = { championId: 103, position: 'MIDDLE' } as const

beforeEach(() => {
  window.localStorage.clear()
})

describe('resolveFavoriteChampion', () => {
  it('keeps a champion with or without a position', () => {
    expect(resolveFavoriteChampion(ahriMid)).toEqual(ahriMid)
    expect(resolveFavoriteChampion({ championId: 103, position: 'NOWHERE' })).toEqual({ championId: 103, position: null })
  })

  it('refuses anything without a real champion id', () => {
    for (const value of [null, 'Ahri', 103, { championId: -1 }, { championId: 1.5 }, { championId: '103' }])
      expect(resolveFavoriteChampion(value)).toBeNull()
  })
})

describe('normalizeFavoriteChampions', () => {
  it('orders newest first and keeps the most recent copy of a duplicate', () => {
    const champions = normalizeFavoriteChampions([
      { ...ahriMid, addedAt: 1 },
      { championId: 238, position: 'MIDDLE', addedAt: 3 },
      { ...ahriMid, addedAt: 5 },
    ])
    expect(champions).toEqual([{ ...ahriMid, addedAt: 5 }, { championId: 238, position: 'MIDDLE', addedAt: 3 }])
  })

  it('treats a champion on two positions as two favorites', () => {
    expect(normalizeFavoriteChampions([{ ...ahriMid, addedAt: 1 }, { ...ahriMid, position: 'TOP', addedAt: 2 }])).toHaveLength(2)
  })

  it('caps the list, dropping the oldest', () => {
    const many = Array.from({ length: FAVORITE_CHAMPIONS_LIMIT + 2 }, (_, i) => ({ championId: i + 1, position: null, addedAt: i }))
    const champions = normalizeFavoriteChampions(many)
    expect(champions).toHaveLength(FAVORITE_CHAMPIONS_LIMIT)
    expect(champions.some(champion => champion.championId === 1)).toBe(false)
  })
})

describe('parseStoredFavoriteChampions', () => {
  it('degrades corruption to an empty list', () => {
    expect(parseStoredFavoriteChampions(null)).toEqual([])
    expect(parseStoredFavoriteChampions('{not json')).toEqual([])
    expect(parseStoredFavoriteChampions('{"championId":103}')).toEqual([])
  })

  it('drops unusable records and keeps the rest', () => {
    const raw = JSON.stringify([{ ...ahriMid, addedAt: 2 }, { championId: 'Zed' }])
    expect(parseStoredFavoriteChampions(raw)).toEqual([{ ...ahriMid, addedAt: 2 }])
  })
})

describe('favorite champions store', () => {
  it('starts empty and reads storage only when asked', () => {
    window.localStorage.setItem(FAVORITE_CHAMPIONS_STORAGE_KEY, JSON.stringify([{ ...ahriMid, addedAt: 1 }]))
    const { store } = makeStore()
    expect(store.champions.value).toEqual([])
    store.loadChampionsFromStorage()
    expect(store.isFavoriteChampion(ahriMid)).toBe(true)
  })

  it('toggles a champion on and off, persisting each write under its own key', () => {
    const { store } = makeStore(() => 1_700)
    expect(store.toggleChampion(ahriMid)).toBe(true)
    expect(store.champions.value).toEqual([{ ...ahriMid, addedAt: 1_700 }])
    expect(JSON.parse(window.localStorage.getItem(FAVORITE_CHAMPIONS_STORAGE_KEY)!)).toEqual(store.champions.value)

    expect(store.toggleChampion(ahriMid)).toBe(false)
    expect(window.localStorage.getItem(FAVORITE_CHAMPIONS_STORAGE_KEY)).toBe('[]')
  })

  it('never touches the followed players', () => {
    const players = JSON.stringify([{ gameName: 'Sheiden', tagLine: '1234', addedAt: 5 }])
    window.localStorage.setItem(FAVORITES_STORAGE_KEY, players)
    const { store } = makeStore()
    store.toggleChampion(ahriMid)
    store.clearChampions()
    expect(window.localStorage.getItem(FAVORITES_STORAGE_KEY)).toBe(players)
  })

  it('refuses an unusable input instead of reporting a star that never landed', () => {
    const { store } = makeStore()
    expect(store.toggleChampion({ championId: 0, position: null })).toBe(false)
    expect(store.champions.value).toEqual([])
  })

  it('reports the list as full at the cap', () => {
    const { store } = makeStore()
    for (let id = 1; id <= FAVORITE_CHAMPIONS_LIMIT; id++) store.toggleChampion({ championId: id, position: null })
    expect(store.championsFull.value).toBe(true)
  })
})
