import { describe, expect, it } from 'vitest'
import type { ChampionResponse } from '~~/shared/types/champions'
import type {
  ChampionStaticData,
  RuneTreeResponse,
  StaticItemData,
  StaticSummonerSpellData,
} from '~~/shared/types/static-data'
import type { BuildSummaryEntityToken, BuildSummarySentence } from '~~/shared/types/champion-build-summary'
import {
  buildSummarySentenceText,
  championBuildSentences,
  championBuildSentenceTokens,
  lanePhrase,
  resolveChampionBuildSummary,
} from '~~/shared/utils/champion-build-summary'

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
  1056: { id: 1056, name: 'Doran\'s Ring', iconUrl: 'https://cdn/item/1056.png', totalGold: 400 },
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
    8112: { id: 8112, name: 'Electrocute', iconUrl: 'https://cdn/perk/8112.png' },
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
    Q: { key: 'Q', name: 'Orb of Deception', iconUrl: 'https://cdn/spell/Q.png' },
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
  it('states the sample, the build and the items', () => {
    const sentences = championBuildSentences(resolve())
    const text = sentences.join(' ')

    expect(text).toContain('Across 247 ranked games on patch 16.16, Ahri mains win 50.6% of their games in the mid lane.')
    expect(text).toContain('Their most common build appears in 38.0% of those games and wins 52.1% of its 94 games.')
    expect(text).toContain('completes Luden\'s Companion, Shadowflame and Zhonya\'s Hourglass in that order')
    expect(text).toContain('starts Doran\'s Ring')
    expect(text).toContain('takes Sorcerer\'s Shoes')
  })

  it('says nothing the icon grid already says in pictures (#1466)', () => {
    // Runes, skill order and summoner spells each had a sentence naming, in
    // words, a row of icons a few hundred pixels away. They are the reason the
    // paragraph read as the build said twice, and they are gone.
    const text = championBuildSentences(resolve()).join(' ')
    expect(text).not.toContain('It runs Electrocute')
    expect(text).not.toContain('Abilities are levelled')
    expect(text).not.toContain('Summoner spells are')
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

    // A single build carries no count sentence at all — the item clause is the
    // last thing said.
    expect(championBuildSentences(resolve()).at(-1))
      .toBe('It starts Doran\'s Ring, completes Luden\'s Companion, Shadowflame and Zhonya\'s Hourglass in that order, and takes Sorcerer\'s Shoes.')
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
    expect(bootsOnly.some(s => s.includes('and takes'))).toBe(false)
    expect(bootsOnly).toContain('It takes Sorcerer\'s Shoes.')
  })

  it('writes no em dash anywhere', () => {
    // The paragraph is read in a 26 rem sidebar column, where a parenthetical
    // set off by dashes breaks the line twice and reads as a stumble. The
    // build's share is its own sentence instead — pin it, because "just add an
    // aside" is the natural way to write the next clause someone adds.
    expect(championBuildSentences(resolve({ minSampleMet: false }, 'Zed')).join(' '))
      .not.toMatch(/[—–]/)
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

describe('championBuildSentenceTokens', () => {
  function entities(sentences: BuildSummarySentence[]): BuildSummaryEntityToken[] {
    return sentences.flat().filter((token): token is BuildSummaryEntityToken => token.kind === 'entity')
  }

  it('concatenates to exactly the plain sentences', () => {
    // The decorated paragraph and the indexable one are the same paragraph.
    // Two builders would drift, and the drift would be invisible — the page
    // would keep looking right while the crawler read something else.
    const summary = resolve({ minSampleMet: false }, 'Zed')
    expect(championBuildSentenceTokens(summary).map(buildSummarySentenceText))
      .toEqual(championBuildSentences(summary))
  })

  it('marks every named entity and leaves the connective prose alone', () => {
    const named = entities(championBuildSentenceTokens(resolve())).map(token => token.text)
    expect(named).toContain('Doran\'s Ring')
    expect(named).toContain('Sorcerer\'s Shoes')
    expect(named).toContain('Luden\'s Companion')
    // "in that order", "starts", "takes" are prose, not entities.
    expect(named.some(text => text.includes('in that order'))).toBe(false)
  })

  it('carries each entity\'s own icon, and none for the ones DDragon left empty', () => {
    const byName = new Map(entities(championBuildSentenceTokens(resolve())).map(t => [t.text, t.iconUrl]))
    expect(byName.get('Doran\'s Ring')).toBe('https://cdn/item/1056.png')

    // An entity DDragon named but shipped no artwork for is still named: the
    // alternative is a build path that silently loses an item to a missing PNG.
    expect(byName.get('Shadowflame')).toBeNull()
    expect(byName.get('Sorcerer\'s Shoes')).toBeNull()
  })

  it('keeps a collapsed run of one item as a single mark', () => {
    const base = champion().builds[0]!
    const tokens = championBuildSentenceTokens(resolve({
      builds: [{
        ...base,
        core: {
          ...base.core,
          starterItems: { itemIds: [1056, 2003, 2003], games: 90, pickRate: 0.36, winRate: 0.52 },
        },
      }],
    }))
    const potions = entities(tokens).filter(token => token.text.includes('Health Potion'))
    expect(potions).toHaveLength(1)
    expect(potions[0]?.text).toBe('two Health Potions')
  })

  it('routes each mark to the static map its record actually lives in', () => {
    // The tone cannot answer this on its own, and a mark pointed at the wrong
    // map silently yields no hover card at all — a failure that is invisible
    // until someone hovers the one word that has no tooltip.
    const bySource = new Map(entities(championBuildSentenceTokens(resolve())).map(t => [t.text, t.source]))
    expect(bySource.get('Doran\'s Ring')).toBe('item')
    expect(bySource.get('Sorcerer\'s Shoes')).toBe('item')
    expect(bySource.get('Zhonya\'s Hourglass')).toBe('item')
  })

  it('keys an entity by what its map is keyed by', () => {
    // `id` is the lookup key, not just a `:key`.
    const byName = new Map(entities(championBuildSentenceTokens(resolve())).map(t => [t.text, t.id]))
    expect(byName.get('Doran\'s Ring')).toBe(1056)
    expect(byName.get('Sorcerer\'s Shoes')).toBe(3020)
  })

  it('marks the pinned opponent as a champion', () => {
    const summary = { ...resolve({}, 'Zed'), opponentIconUrl: 'https://cdn/champion/Zed.png' }
    const opponent = entities(championBuildSentenceTokens(summary)).find(token => token.text === 'Zed')
    expect(opponent?.tone).toBe('champion')
    expect(opponent?.iconUrl).toBe('https://cdn/champion/Zed.png')
  })

  it('sets every figure as a measurement', () => {
    const values = championBuildSentenceTokens(resolve())
      .flat()
      .filter(token => token.kind === 'value')
      .map(token => token.text)
    expect(values).toContain('247')
    expect(values).toContain('16.16')
    expect(values).toContain('50.6%')
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
