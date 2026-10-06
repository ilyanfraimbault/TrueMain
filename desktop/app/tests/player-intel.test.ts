import { describe, expect, it } from 'vitest'
import type { GamePlayer } from '~/types/game'
import type { LoadingPlayer, PlayerForm } from '~/types/loading'
import type { RankedQueue } from '~/types/record'
import type { TruemainMark } from '~/utils/player-intel'
import { MIN_STREAK, championRecord, loadingLineOf, lookupPlayers, markFigures, markOf, recentWinRate, roleFit, seasonWinRate, streakOf } from '~/utils/player-intel'

function form(overrides: Partial<PlayerForm> = {}): PlayerForm {
  return {
    games: 20,
    championGames: 0,
    championWins: 0,
    championKills: 0,
    championDeaths: 0,
    championAssists: 0,
    positions: [],
    streak: 0,
    recent: [],
    ...overrides,
  }
}

function line(overrides: Partial<LoadingPlayer> = {}): LoadingPlayer {
  return {
    riotId: '',
    championId: 1,
    team: 'ORDER',
    position: 'TOP',
    isMe: false,
    anonymous: false,
    form: null,
    failed: false,
    rank: null,
    rankRead: false,
    rankFailed: false,
    laning: null,
    ...overrides,
  }
}

function player(overrides: Partial<GamePlayer> = {}): GamePlayer {
  return {
    riotId: '',
    champion: 'Annie',
    championName: 'Annie',
    team: 'ORDER',
    position: 'TOP',
    isMe: false,
    isBot: false,
    spells: [],
    level: 1,
    items: [],
    kills: 0,
    deaths: 0,
    assists: 0,
    creepScore: 0,
    dead: false,
    respawnAt: null,
    ...overrides,
  }
}

const championIds: Record<string, number> = { Annie: 1, Viego: 234, Rammus: 33 }
const championIdOf = (alias: string) => championIds[alias]

describe('loadingLineOf', () => {
  it('matches a named player by Riot ID, whatever the case', () => {
    const lines = [line({ riotId: 'Other#EUW' }), line({ riotId: 'Faker#KR1', championId: 234 })]
    expect(loadingLineOf(player({ riotId: 'faker#kr1' }), lines, championIdOf)).toBe(lines[1])
  })

  it('matches an anonymous player by side and champion', () => {
    const lines = [
      line({ anonymous: true, team: 'CHAOS', championId: 234 }),
      line({ anonymous: true, team: 'ORDER', championId: 234 }),
    ]
    expect(loadingLineOf(player({ champion: 'Viego', team: 'ORDER' }), lines, championIdOf)).toBe(lines[1])
  })

  it('finds nothing for an unknown champion or a missing line', () => {
    expect(loadingLineOf(player({ champion: 'Unknown' }), [line()], championIdOf)).toBeNull()
    expect(loadingLineOf(player({ champion: 'Rammus' }), [line()], championIdOf)).toBeNull()
  })
})

describe('roleFit', () => {
  const positions = (top: number, jungle: number) => [
    { position: 'TOP', games: top },
    { position: 'JUNGLE', games: jungle },
  ]

  it('says nothing under three role-assigned games', () => {
    expect(roleFit('TOP', positions(1, 1))).toBeNull()
    expect(roleFit('', positions(5, 5))).toBeNull()
  })

  it('makes a role played half the time the main one', () => {
    expect(roleFit('TOP', positions(5, 5))).toMatchObject({ kind: 'main', usual: null, games: 5, roleGames: 10 })
  })

  it('calls a role played a quarter of the time secondary', () => {
    expect(roleFit('JUNGLE', positions(6, 2))).toMatchObject({ kind: 'secondary', usual: 'TOP' })
  })

  it('calls a role under a quarter an autofill, naming the usual one', () => {
    expect(roleFit('JUNGLE', positions(7, 2))).toMatchObject({ kind: 'autofill', usual: 'TOP' })
    expect(roleFit('MIDDLE', positions(7, 2))).toMatchObject({ kind: 'autofill', games: 0 })
  })
})

describe('championRecord', () => {
  it('is null on a first time on the champion', () => {
    expect(championRecord(form())).toBeNull()
  })

  it('averages the games on the champion, deaths floored at one for the KDA', () => {
    const record = championRecord(form({ championGames: 3, championWins: 2, championKills: 15, championDeaths: 0, championAssists: 6 }))
    expect(record).toMatchObject({ games: 3, winRate: 67, kda: 21, kills: 5, deaths: 0, assists: 2 })
  })
})

describe('streakOf', () => {
  it('needs three games in a row', () => {
    expect(streakOf(form({ streak: MIN_STREAK - 1 }))).toBeNull()
    expect(streakOf(form({ streak: MIN_STREAK }))).toEqual({ wins: true, games: 3 })
    expect(streakOf(form({ streak: -4 }))).toEqual({ wins: false, games: 4 })
  })
})

describe('win rates', () => {
  it('reads the recent games, null with none', () => {
    const game = (win: boolean) => ({ championId: 1, position: 'TOP', win, kills: 0, deaths: 0, assists: 0, playedAt: 0 })
    expect(recentWinRate(form())).toBeNull()
    expect(recentWinRate(form({ recent: [game(true), game(false), game(true)] }))).toBe(67)
  })

  it('reads the season record, null with no game', () => {
    const rank = (wins: number, losses: number) => ({ wins, losses }) as RankedQueue
    expect(seasonWinRate(rank(0, 0))).toBeNull()
    expect(seasonWinRate(rank(30, 10))).toBe(75)
  })
})

describe('true-main mark', () => {
  const mark = (overrides: Partial<TruemainMark> = {}): TruemainMark => ({
    riotId: 'AhriMain#EUW',
    nameTag: 'AhriMain-EUW',
    championId: 103,
    championMatches: 38,
    totalMatches: 50,
    playRate: 0.76,
    isOtp: false,
    masteryPoints: null,
    dedication: 86.6,
    ...overrides,
  })

  it('asks about the named players on a champion, never an anonymous one', () => {
    const players = lookupPlayers([
      line({ riotId: 'Zed#KR1', championId: 238 }),
      line({ riotId: 'AhriMain#EUW', championId: 103 }),
      line({ riotId: '', championId: 81, anonymous: true }),
      line({ riotId: 'Hidden#EUW', championId: 54, anonymous: true }),
      line({ riotId: '', championId: 61 }),
      line({ riotId: 'NoChampion#EUW', championId: 0 }),
    ])
    expect(players).toEqual(['AhriMain#EUW:103', 'Zed#KR1:238'])
  })

  it('marks the player on the champion they main, whatever the casing', () => {
    expect(markOf([mark()], line({ riotId: 'ahrimain#euw', championId: 103 }))).toEqual(mark())
  })

  it('leaves a true main of another champion unmarked', () => {
    expect(markOf([mark()], line({ riotId: 'AhriMain#EUW', championId: 7 }))).toBeNull()
  })

  it('never marks an anonymous line or a missing one', () => {
    expect(markOf([mark()], line({ riotId: 'AhriMain#EUW', championId: 103, anonymous: true }))).toBeNull()
    expect(markOf([mark()], null)).toBeNull()
  })

  it('explains the mark with the figures returned only', () => {
    expect(markFigures(mark())).toEqual(['38 of their last 50 ranked games', 'Truemain score 87'])
    expect(markFigures(mark({ isOtp: true, totalMatches: 0 }))).toEqual(['One-trick', 'Truemain score 87'])
  })
})
