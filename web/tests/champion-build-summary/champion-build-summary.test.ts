import { describe, expect, it } from 'vitest'
import type { ChampionResponse } from '../../shared/types/champions'
import type {
  ChampionStaticData,
  RuneTreeResponse,
  StaticItemData,
  StaticSummonerSpellData,
} from '../../shared/types/static-data'
import {
  championBuildSentences,
  lanePhrase,
  resolveChampionBuildSummary,
} from '../../shared/utils/champion-build-summary'

/**
 * The server-rendered build summary (#1123) is the only build content in the
 * champion page's HTML, and it is *prose* — which makes its failure mode
 * different from the rest of the page. A broken icon is visibly broken; a
 * sentence is never visibly broken. It can name an item the build doesn't
 * contain, print the core path out of order, attribute a rune to the wrong
 * tree, or invent a label for an id DDragon didn't know — and still read
 * perfectly. These tests pin the claims the sentences make.
 */

const ITEMS: Record<number, StaticItemData> = {
  1056: { id: 1056, name: 'Doran\'s Ring', iconUrl: '', totalGold: 400 },
  3020: { id: 3020, name: 'Sorcerer\'s Shoes', iconUrl: '', totalGold: 1100 },
  3157: { id: 3157, name: 'Zhonya\'s Hourglass', iconUrl: '', totalGold: 3250 },
  4645: { id: 4645, name: 'Shadowflame', iconUrl: '', totalGold: 3200 },
  6655: { id: 6655, name: 'Luden\'s Companion', iconUrl: '', totalGold: 3200 },
  2003: { id: 2003, name: 'Health Potion', iconUrl: '', totalGold: 50 },
}

const RUNE_TREE = {
  styles: [],
  shardSlots: [],
  perks: {
    8112: { id: 8112, name: 'Electrocute', iconUrl: '' },
    8135: { id: 8135, name: 'Treasure Hunter', iconUrl: '' },
    8138: { id: 8138, name: 'Eyeball Collection', iconUrl: '' },
    8139: { id: 8139, name: 'Absolute Focus', iconUrl: '' },
    8210: { id: 8210, name: 'Transcendence', iconUrl: '' },
    8237: { id: 8237, name: 'Scorch', iconUrl: '' },
  },
  perkStyles: {
    8100: { id: 8100, name: 'Domination', iconUrl: '' },
    8200: { id: 8200, name: 'Sorcery', iconUrl: '' },
  },
} satisfies RuneTreeResponse

const SUMMONERS: Record<number, StaticSummonerSpellData> = {
  4: { id: 4, name: 'Flash', iconUrl: '' },
  14: { id: 14, name: 'Ignite', iconUrl: '' },
}

const CHAMPION_STATIC: ChampionStaticData = {
  championName: 'Ahri',
  championIconUrl: '',
  partype: 'Mana',
  championSpells: {
    Q: { key: 'Q', name: 'Orb of Deception', iconUrl: '' },
    W: { key: 'W', name: 'Fox-Fire', iconUrl: '' },
    E: { key: 'E', name: 'Charm', iconUrl: '' },
    R: { key: 'R', name: 'Spirit Rush', iconUrl: '' },
  },
}

function champion(overrides: Partial<ChampionResponse> = {}): ChampionResponse {
  return {
    championId: 103,
    patch: '16.16',
    position: 'MIDDLE',
    eloBracket: 'ALL',
    eloCoverage: 1,
    minSampleMet: true,
    totalGames: 247,
    totalWins: 125,
    builds: [{
      firstItemId: 6655,
      primaryKeystoneId: 8112,
      games: 94,
      pickRate: 0.38,
      winRate: 0.521,
      variations: { boots: [], starterItems: [], summonerSpells: [], skillOrder: [] },
      buildTree: [],
      runePages: [],
      core: {
        itemPath: { itemIds: [6655, 4645, 3157], games: 94, pickRate: 0.38, winRate: 0.521 },
        boots: { itemIds: [3020], games: 80, pickRate: 0.32, winRate: 0.52 },
        starterItems: { itemIds: [1056], games: 90, pickRate: 0.36, winRate: 0.52 },
        summonerSpells: { spell1Id: 4, spell2Id: 14, games: 90, pickRate: 0.36, winRate: 0.52 },
        skillOrder: { sequence: ['Q', 'W', 'E'], games: 88, pickRate: 0.35, winRate: 0.52 },
        runePage: {
          primaryStyleId: 8100,
          primaryKeystoneId: 8112,
          primaryPerk1Id: 8139,
          primaryPerk2Id: 8138,
          primaryPerk3Id: 8135,
          secondaryStyleId: 8200,
          secondaryPerk1Id: 8210,
          secondaryPerk2Id: 8237,
          statOffense: 5008,
          statFlex: 5008,
          statDefense: 5001,
          games: 70,
          pickRate: 0.28,
          winRate: 0.53,
        },
      },
    }],
    ...overrides,
  }
}

function resolve(overrides: Partial<ChampionResponse> = {}, opponentName: string | null = null) {
  return resolveChampionBuildSummary({
    championId: 103,
    champion: champion(overrides),
    championStatic: CHAMPION_STATIC,
    itemsMap: ITEMS,
    runeTree: RUNE_TREE,
    summonersMap: SUMMONERS,
    requestedEloBracket: 'ALL',
    opponentName,
  })
}

describe('resolveChampionBuildSummary', () => {
  it('keeps the core path in build order', () => {
    // The order *is* the claim ("completes X, Y and Z in that order"), so a
    // resolver that sorted or set-deduped would produce a fluent lie.
    expect(resolve().build?.coreItems.map(item => item.name)).toEqual([
      'Luden\'s Companion',
      'Shadowflame',
      'Zhonya\'s Hourglass',
    ])
  })

  it('drops an item DDragon does not know instead of labelling it', () => {
    const summary = resolve({
      builds: [{
        ...champion().builds[0]!,
        core: {
          ...champion().builds[0]!.core,
          itemPath: { itemIds: [6655, 999999, 3157], games: 94, pickRate: 0.38, winRate: 0.521 },
        },
      }],
    })
    const names = summary.build?.coreItems.map(item => item.name)
    expect(names).toEqual(['Luden\'s Companion', 'Zhonya\'s Hourglass'])
    expect(names?.some(name => name.includes('999999'))).toBe(false)
  })

  it('separates the primary tree runes from the secondary ones', () => {
    const build = resolve().build
    expect(build?.keystone?.name).toBe('Electrocute')
    expect(build?.primaryStyle?.name).toBe('Domination')
    expect(build?.primaryRunes.map(r => r.name)).toEqual([
      'Absolute Focus',
      'Eyeball Collection',
      'Treasure Hunter',
    ])
    expect(build?.secondaryStyle?.name).toBe('Sorcery')
    expect(build?.secondaryRunes.map(r => r.name)).toEqual(['Transcendence', 'Scorch'])
  })

  it('describes the build the page opens on, not the best one', () => {
    // BuildTabs defaults to the first tab; picking the highest win rate here
    // would describe a build the reader is not looking at.
    const second = { ...champion().builds[0]!, winRate: 0.99, pickRate: 0.01, games: 5 }
    const summary = resolve({ builds: [champion().builds[0]!, second] })
    expect(summary.build?.winRate).toBe(0.521)
    expect(summary.buildCount).toBe(2)
  })

  it('returns an empty summary rather than throwing when every upstream is down', () => {
    const summary = resolveChampionBuildSummary({
      championId: 103,
      champion: null,
      championStatic: null,
      itemsMap: null,
      runeTree: null,
      summonersMap: null,
      requestedEloBracket: 'GOLD_PLUS',
    })
    expect(summary.build).toBeNull()
    expect(summary.championName).toBeNull()
    expect(summary.games).toBe(0)
    expect(summary.eloBracket).toBe('GOLD_PLUS')
  })
})

describe('championBuildSentences', () => {
  it('states the sample, the build, the runes, the skills and the spells', () => {
    const sentences = championBuildSentences(resolve())
    const text = sentences.join(' ')

    expect(text).toContain('Across 247 ranked games on patch 16.16, Ahri mains win 50.6% of their games in the mid lane.')
    expect(text).toContain('completes Luden\'s Companion, Shadowflame and Zhonya\'s Hourglass in that order')
    expect(text).toContain('starts Doran\'s Ring')
    expect(text).toContain('takes Sorcerer\'s Shoes')
    expect(text).toContain('It runs Electrocute out of Domination')
    expect(text).toContain('and Sorcery secondary for Transcendence and Scorch')
    expect(text).toContain('Abilities are levelled Q (Orb of Deception) first, then W (Fox-Fire), then E (Charm).')
    expect(text).toContain('Summoner spells are Flash and Ignite.')
  })

  it('separates the secondary tree with a comma, not a second bare and', () => {
    // A first cut chained every clause with `and`, producing "…Eyeball
    // Collection and Treasure Hunter and Sorcery secondary…" — which reads as
    // one four-item list of primary runes and silently reassigns the tree. The
    // comma before `and` is the whole fix, so pin it both ways.
    const text = championBuildSentences(resolve()).join(' ')
    expect(text).toContain('Treasure Hunter, and Sorcery secondary')
    expect(text).not.toMatch(/[^,] and \S+ secondary/)
  })

  it('names the rank only when the slice is scoped to one', () => {
    expect(championBuildSentences(resolve()).join(' ')).not.toContain(' in All')
    expect(championBuildSentences(resolve({ eloBracket: 'GOLD_PLUS' }))[0])
      .toContain('in Gold and above')
    expect(championBuildSentences(resolve({ eloBracket: 'DIAMOND' }))[0])
      .toContain('in Diamond')
  })

  it('opens the build-count sentence with a word, not a numeral', () => {
    const first = champion().builds[0]!
    const three = championBuildSentences(resolve({ builds: [first, first, first] }))
    expect(three.at(-1)).toBe('Two other builds are played often enough in the mid lane to be measured on their own.')

    const two = championBuildSentences(resolve({ builds: [first, first] }))
    expect(two.at(-1)).toBe('One other build is played often enough in the mid lane to be measured on its own.')

    expect(championBuildSentences(resolve()).at(-1)).toBe('Summoner spells are Flash and Ignite.')
  })

  it('names the pinned lane opponent the slice was computed against', () => {
    // Without this the paragraph describes the champion's global build in prose
    // directly under panels re-sliced to the matchup (#923) — the exact
    // "summary contradicts the page" failure the block exists to avoid.
    const sentences = championBuildSentences(resolve({ totalGames: 41, totalWins: 20 }, 'Zed'))
    expect(sentences[0]).toBe(
      'Across 41 ranked games on patch 16.16, Ahri mains win 48.8% of their games in the mid lane against Zed.',
    )
    expect(championBuildSentences(resolve())[0]).not.toContain(' against ')
  })

  it('carries the low-sample caveat the page shows as an icon', () => {
    const sentences = championBuildSentences(resolve({ minSampleMet: false }))
    // Second, so it qualifies every figure after it rather than trailing them.
    expect(sentences[1]).toContain('below the sample TrueMain requires')
    expect(championBuildSentences(resolve()).join(' ')).not.toContain('indicative')
  })

  it('counts a repeated starter item instead of naming it twice', () => {
    // A real starter set holds two potions as two entries. Repeating the name is
    // accurate and unreadable; the count has to stay exact either way.
    const base = champion().builds[0]!
    const sentences = championBuildSentences(resolve({
      builds: [{
        ...base,
        core: {
          ...base.core,
          starterItems: { itemIds: [1056, 2003, 2003], games: 90, pickRate: 0.36, winRate: 0.52 },
        },
      }],
    }))
    const text = sentences.join(' ')
    expect(text).toContain('starts Doran\'s Ring and two Health Potions')
    expect(text).not.toContain('Health Potion and Health Potion')
  })

  it('does not open the item clause with a bare and when boots are all it has', () => {
    const base = champion().builds[0]!
    const bootsOnly = championBuildSentences(resolve({
      builds: [{ ...base, core: { ...base.core, starterItems: null, itemPath: null } }],
    }))
    expect(bootsOnly.some(s => s.includes('— and takes'))).toBe(false)
    expect(bootsOnly.some(s => s.includes('— takes Sorcerer\'s Shoes.'))).toBe(true)
  })

  it('says nothing at all without a name or without games', () => {
    // The block renders only what it can measure: an empty array is what makes
    // the component disappear instead of printing a heading over nothing.
    expect(championBuildSentences(resolve({ totalGames: 0, totalWins: 0 }))).toEqual([])
    expect(championBuildSentences({ ...resolve(), championName: null })).toEqual([])
  })

  it('still states the sample when the slice carries no build', () => {
    expect(championBuildSentences(resolve({ builds: [] }))).toEqual([
      'Across 247 ranked games on patch 16.16, Ahri mains win 50.6% of their games in the mid lane.',
    ])
  })
})

describe('lanePhrase', () => {
  it('never surfaces Riot\'s raw UTILITY', () => {
    expect(lanePhrase('UTILITY')).toBe('as support')
    expect(lanePhrase('MIDDLE')).toBe('in the mid lane')
    expect(lanePhrase(null)).toBe('')
    expect(lanePhrase('NONSENSE')).toBe('')
  })
})
