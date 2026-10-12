import { describe, expect, it } from 'vitest'
import type { ChampionBuildSummary, ChampionBuildSummaryMatchups } from '~~/shared/types/champion-build-summary'
import type { ChampionMatchupEntry } from '~~/shared/types/champions'
import type { ChampionStaticListItem } from '~~/shared/types/static-data'
import { buildSummarySentenceText } from '~~/shared/utils/champion-build-summary'
import {
  championMatchupSentenceTokens,
  resolveSummaryMatchups,
  SUMMARY_MATCHUPS_PER_SIDE,
} from '~~/shared/utils/champion-matchup-summary'
import { rankMatchups } from '~~/shared/utils/matchup-ranking'

/**
 * The matchup sentences of the champion page's server HTML (#1954). They replace
 * the panel's client-only empty state for a crawler, so the claims they make are
 * the ones pinned here: the opponents are the panel's own first rows, each figure
 * is the measured one, and an opponent nobody can name is dropped, not labelled.
 */

const CHAMPIONS: ChampionStaticListItem[] = [
  { championId: 238, name: 'Zed', iconUrl: 'https://cdn/champion/Zed.png' },
  { championId: 157, name: 'Yasuo', iconUrl: '' },
  { championId: 99, name: 'Lux', iconUrl: '' },
  { championId: 38, name: 'Kassadin', iconUrl: '' },
  { championId: 61, name: 'Orianna', iconUrl: '' },
  { championId: 7, name: 'LeBlanc', iconUrl: '' },
  { championId: 84, name: 'Akali', iconUrl: '' },
  { championId: 134, name: 'Syndra', iconUrl: '' },
]

function matchup(opponentChampionId: number, winRate: number, games: number): ChampionMatchupEntry {
  // A symmetric band is enough to order the fixtures; the tests are about which
  // rows the sentences take, not about the interval itself.
  return {
    opponentChampionId,
    games,
    wins: Math.round(winRate * games),
    winRate,
    playRate: 0.05,
    winRateLowerBound: winRate - 0.05,
    winRateUpperBound: winRate + 0.05,
    laneWinRate: null,
  } as ChampionMatchupEntry
}

const ENTRIES = [
  matchup(238, 0.581, 312),
  matchup(157, 0.55, 280),
  matchup(99, 0.548, 190),
  matchup(84, 0.53, 120),
  matchup(134, 0.52, 100),
  matchup(61, 0.47, 140),
  matchup(7, 0.452, 95),
  matchup(38, 0.441, 150),
]

function summary(matchups: ChampionBuildSummaryMatchups | undefined, overrides: Partial<ChampionBuildSummary> = {}): ChampionBuildSummary {
  return {
    championId: 103,
    championName: 'Ahri',
    position: 'MIDDLE',
    patch: '16.19',
    eloBracket: 'ALL',
    opponentName: null,
    opponentIconUrl: null,
    minSampleMet: true,
    games: 4603,
    wins: 2400,
    winRate: 0.521,
    build: null,
    buildCount: 0,
    matchups,
    ...overrides,
  }
}

function sentences(input: ChampionBuildSummary): string[] {
  return championMatchupSentenceTokens(input).map(buildSummarySentenceText)
}

describe('resolveSummaryMatchups', () => {
  it('names the first rows of the panel\'s own lists, in the panel\'s order', () => {
    const panel = rankMatchups(ENTRIES)

    const resolved = resolveSummaryMatchups(ENTRIES, CHAMPIONS)!

    expect(resolved.best.map(m => m.id)).toEqual(panel.best.slice(0, SUMMARY_MATCHUPS_PER_SIDE).map(m => m.opponentChampionId))
    expect(resolved.worst.map(m => m.id)).toEqual(panel.worst.slice(0, SUMMARY_MATCHUPS_PER_SIDE).map(m => m.opponentChampionId))
  })

  it('never names, as a worst matchup, a champion the panel lists among the best', () => {
    // Six opponents: the panel's best five leave a single worst row. Ranking at
    // the prose's length instead would push best rows four and five into "worst".
    const six = ENTRIES.slice(0, 6)

    const resolved = resolveSummaryMatchups(six, CHAMPIONS)!

    expect(resolved.worst.map(m => m.id)).toEqual([61])
  })

  it('carries the measured games and win rate, and the icon when there is one', () => {
    const resolved = resolveSummaryMatchups(ENTRIES, CHAMPIONS)!

    expect(resolved.best[0]).toEqual({
      id: 238,
      name: 'Zed',
      iconUrl: 'https://cdn/champion/Zed.png',
      games: 312,
      winRate: 0.581,
    })
    expect(resolved.best[1]!.iconUrl).toBeNull()
  })

  it('drops an opponent DDragon cannot name instead of labelling it', () => {
    const resolved = resolveSummaryMatchups(ENTRIES, CHAMPIONS.filter(c => c.championId !== 238))!

    expect(resolved.best.map(m => m.name)).toEqual(['Yasuo', 'Lux', 'Akali'])
  })

  it.each([
    ['no matchup', [], CHAMPIONS],
    ['no matchup response', null, CHAMPIONS],
    ['no champion list', ENTRIES, null],
  ])('is absent with %s', (_label, entries, champions) => {
    expect(resolveSummaryMatchups(entries, champions)).toBeUndefined()
  })
})

describe('championMatchupSentenceTokens', () => {
  it('states the best and the worst matchups with their records', () => {
    expect(sentences(summary(resolveSummaryMatchups(ENTRIES, CHAMPIONS)))).toEqual([
      'In the mid lane, Ahri mains fare best against Zed (won 58.1% of 312 games), '
      + 'Yasuo (won 55.0% of 280 games) and Lux (won 54.8% of 190 games).',
      'Their hardest matchups, the champions that counter Ahri, are Kassadin (won 44.1% of 150 games), '
      + 'LeBlanc (won 45.2% of 95 games) and Orianna (won 47.0% of 140 games).',
    ])
  })

  it('makes every opponent a champion mark carrying its id, which the view links', () => {
    const marks = championMatchupSentenceTokens(summary(resolveSummaryMatchups(ENTRIES, CHAMPIONS)))
      .flat()
      .filter(token => token.kind === 'entity')

    expect(marks.map(token => token.kind === 'entity' && token.id)).toEqual([238, 157, 99, 38, 7, 61])
    expect(marks.every(token => token.kind === 'entity' && token.source === 'champion')).toBe(true)
  })

  it('reads in the singular for one worst matchup and one game', () => {
    const one = { best: [], worst: [{ id: 38, name: 'Kassadin', iconUrl: null, games: 1, winRate: 0 }] }

    expect(sentences(summary(one))).toEqual([
      'Their hardest matchup, the champion that counters Ahri, is Kassadin (won 0.0% of 1 game).',
    ])
  })

  it('opens without a lane clause when the lane is unknown, and says "as support" for supports', () => {
    const matchups = resolveSummaryMatchups(ENTRIES, CHAMPIONS)

    expect(sentences(summary(matchups, { position: null }))[0]).toMatch(/^Ahri mains fare best against Zed/)
    expect(sentences(summary(matchups, { position: 'UTILITY' }))[0]).toMatch(/^As support, Ahri mains fare best/)
  })

  it('says nothing without matchups, a name, or games', () => {
    const matchups = resolveSummaryMatchups(ENTRIES, CHAMPIONS)

    expect(sentences(summary(undefined))).toEqual([])
    expect(sentences(summary(matchups, { championName: null }))).toEqual([])
    expect(sentences(summary(matchups, { games: 0 }))).toEqual([])
  })
})
