import { describe, expect, it } from 'vitest'
import type { ChampionTierListResponse } from '~~/shared/types/champions'
import type { ChampionStaticListItem } from '~~/shared/types/static-data'
import {
  championIndexLinks,
  championIndexPatch,
  championIndexTiers,
} from '~~/shared/utils/champion-index'

function champion(championId: number, name: string): ChampionStaticListItem {
  return { championId, name, iconUrl: `https://cdn/${name}.png` }
}

const STATIC_LIST: ChampionStaticListItem[] = [
  champion(103, 'Ahri'),
  champion(238, 'Zed'),
  champion(11, 'Master Yi'),
  champion(145, 'Kai\'Sa'),
]

function tierList(
  tiers: Array<{ tier: string, entries: Array<{ championId: number, position: string }> }>,
  patchVersion = '16.16',
): ChampionTierListResponse {
  return {
    patchVersion,
    position: null,
    tiers: tiers.map(group => ({
      tier: group.tier,
      entries: group.entries.map(entry => ({
        championId: entry.championId,
        position: entry.position,
        tier: group.tier,
        tierScore: 0,
        winRate: 0.5,
        pickRate: 0.1,
        banRate: 0.05,
        games: 1000,
      })),
    })),
  } as ChampionTierListResponse
}

describe('championIndexLinks', () => {
  it('lists every champion once, sorted by name', () => {
    expect(championIndexLinks(STATIC_LIST).map(link => link.name))
      .toEqual(['Ahri', 'Kai\'Sa', 'Master Yi', 'Zed'])
  })

  it('carries the id rather than a slug, so links are built from the app-wide map', () => {
    expect(championIndexLinks(STATIC_LIST)[0]).toEqual({ championId: 103, name: 'Ahri' })
  })

  it('drops champions with no usable name — an anchor with no text is worse than none', () => {
    const links = championIndexLinks([...STATIC_LIST, champion(999, '')])
    expect(links.some(link => link.championId === 999)).toBe(false)
  })

  it('degrades to an empty list when DDragon is unavailable', () => {
    expect(championIndexLinks(null)).toEqual([])
    expect(championIndexLinks(undefined)).toEqual([])
  })
})

describe('championIndexTiers', () => {
  const RANKING = tierList([
    { tier: 'S', entries: [{ championId: 103, position: 'MIDDLE' }, { championId: 238, position: 'MIDDLE' }] },
    { tier: 'A', entries: [{ championId: 11, position: 'JUNGLE' }] },
  ])

  it('keeps the backend ordering — strongest tier first, strongest-first within it', () => {
    const groups = championIndexTiers(RANKING, STATIC_LIST)
    expect(groups.map(group => group.tier)).toEqual(['S', 'A'])
    expect(groups[0]!.entries.map(entry => entry.name)).toEqual(['Ahri', 'Zed'])
  })

  it('resolves names and keeps the lane the tier was computed for', () => {
    const groups = championIndexTiers(RANKING, STATIC_LIST)
    expect(groups[1]!.entries[0]).toEqual({ championId: 11, name: 'Master Yi', position: 'JUNGLE' })
  })

  it('lists a flex champion once, under its strongest tier', () => {
    const flex = tierList([
      { tier: 'S', entries: [{ championId: 103, position: 'MIDDLE' }] },
      { tier: 'B', entries: [{ championId: 103, position: 'TOP' }, { championId: 238, position: 'MIDDLE' }] },
    ])
    const groups = championIndexTiers(flex, STATIC_LIST)
    expect(groups[0]!.entries.map(entry => entry.name)).toEqual(['Ahri'])
    expect(groups[1]!.entries.map(entry => entry.name)).toEqual(['Zed'])
  })

  it('drops champions the name lookup does not know', () => {
    const unknown = tierList([{ tier: 'S', entries: [{ championId: 4242, position: 'TOP' }] }])
    expect(championIndexTiers(unknown, STATIC_LIST)).toEqual([])
  })

  it('caps the total entries across tiers and drops groups the cap empties', () => {
    const groups = championIndexTiers(RANKING, STATIC_LIST, 2)
    expect(groups.map(group => group.tier)).toEqual(['S'])
    expect(groups[0]!.entries).toHaveLength(2)
  })

  it('treats a null limit as no cap and a zero limit as nothing', () => {
    expect(championIndexTiers(RANKING, STATIC_LIST, null)).toHaveLength(2)
    expect(championIndexTiers(RANKING, STATIC_LIST, 0)).toEqual([])
  })

  it('degrades to no groups when either upstream is unavailable', () => {
    expect(championIndexTiers(null, STATIC_LIST)).toEqual([])
    expect(championIndexTiers(RANKING, null)).toEqual([])
  })
})

describe('championIndexPatch', () => {
  it('reads the patch the ranking was computed for', () => {
    expect(championIndexPatch(tierList([], '16.15'))).toBe('16.15')
  })

  it('is null rather than blank when the backend says nothing', () => {
    expect(championIndexPatch(tierList([], ''))).toBeNull()
    expect(championIndexPatch(null)).toBeNull()
  })
})
