import { beforeEach, describe, expect, it } from 'vitest'
import type { RankedQueue } from '~/types/record'
import { dailyStandings, latestLpMoves, lpDeltaOf, readRankHistory, recordRankSnapshot } from '~/utils/lp-history'
import { DAY, MINUTE, game, snapshot } from './fixtures'

const T = Date.UTC(2026, 9, 1, 12)

describe('lpDeltaOf', () => {
  const solo = game({ gameId: 1, playedAt: T })

  it('measures the standing after the game against the one before it', () => {
    const history = [snapshot(T - MINUTE, 50), snapshot(T + 32 * MINUTE, 70)]
    expect(lpDeltaOf(solo, [solo], history)).toBe(20)
  })

  it('is null across a promotion', () => {
    const history = [snapshot(T - MINUTE, 90, 'GOLD', 'II'), snapshot(T + 32 * MINUTE, 10, 'GOLD', 'I')]
    expect(lpDeltaOf(solo, [solo], history)).toBeNull()
  })

  it('is null across a demotion into the tier below', () => {
    const history = [snapshot(T - MINUTE, 5, 'GOLD', 'IV'), snapshot(T + 32 * MINUTE, 75, 'SILVER', 'I')]
    expect(lpDeltaOf(solo, [solo], history)).toBeNull()
  })

  it('is null without a snapshot before the game', () => {
    expect(lpDeltaOf(solo, [solo], [snapshot(T + 32 * MINUTE, 70)])).toBeNull()
  })

  it('is null without a snapshot after the game', () => {
    expect(lpDeltaOf(solo, [solo], [snapshot(T - MINUTE, 50)])).toBeNull()
  })

  it('is null for a remake and for any queue but Solo/Duo', () => {
    const history = [snapshot(T - MINUTE, 50), snapshot(T + 32 * MINUTE, 70)]
    const remake = game({ gameId: 1, playedAt: T, remake: true })
    const flex = game({ gameId: 1, playedAt: T, queueId: 440 })
    expect(lpDeltaOf(remake, [remake], history)).toBeNull()
    expect(lpDeltaOf(flex, [flex], history)).toBeNull()
  })

  it('measures back-to-back Solo/Duo games one at a time', () => {
    const first = game({ gameId: 1, playedAt: T })
    const second = game({ gameId: 2, playedAt: T + 35 * MINUTE })
    const games = [second, first]
    const history = [snapshot(T - MINUTE, 50), snapshot(T + 32 * MINUTE, 70), snapshot(T + 67 * MINUTE, 55)]
    expect(lpDeltaOf(first, games, history)).toBe(20)
    expect(lpDeltaOf(second, games, history)).toBe(-15)
  })

  it('never folds the next game into this one when the standing between them is missing', () => {
    const first = game({ gameId: 1, playedAt: T })
    const second = game({ gameId: 2, playedAt: T + 35 * MINUTE })
    const games = [second, first]
    const history = [snapshot(T - MINUTE, 50), snapshot(T + 67 * MINUTE, 55)]
    expect(lpDeltaOf(first, games, history)).toBeNull()
    expect(lpDeltaOf(second, games, history)).toBeNull()
  })

  it('is not disturbed by a Flex or normal game in between', () => {
    const first = game({ gameId: 1, playedAt: T })
    const flex = game({ gameId: 2, playedAt: T + 35 * MINUTE, queueId: 440 })
    const normal = game({ gameId: 3, playedAt: T + 70 * MINUTE, queueId: 400 })
    const second = game({ gameId: 4, playedAt: T + 105 * MINUTE })
    const games = [second, normal, flex, first]
    // The Solo/Duo standing does not move over the Flex and normal games, so no snapshot is noted for them.
    const history = [snapshot(T - MINUTE, 50), snapshot(T + 32 * MINUTE, 70), snapshot(T + 137 * MINUTE, 88)]
    expect(lpDeltaOf(first, games, history)).toBe(20)
    expect(lpDeltaOf(second, games, history)).toBe(18)
  })
})

describe('recordRankSnapshot', () => {
  const riotId = 'Player#EUW'
  const standing = (leaguePoints: number, tier = 'GOLD', division = 'II'): RankedQueue => ({
    queueType: 'RANKED_SOLO_5x5',
    tier,
    division,
    leaguePoints,
    wins: 10,
    losses: 10,
    isProvisional: false,
  })

  beforeEach(() => localStorage.clear())

  it('appends the standing only when it moved', () => {
    recordRankSnapshot(riotId, standing(50), new Date(T))
    recordRankSnapshot(riotId, standing(50), new Date(T + MINUTE))
    expect(readRankHistory(riotId)).toHaveLength(1)

    recordRankSnapshot(riotId, standing(70), new Date(T + 2 * MINUTE))
    recordRankSnapshot(riotId, standing(70, 'GOLD', 'I'), new Date(T + 3 * MINUTE))
    expect(readRankHistory(riotId).map(entry => [entry.division, entry.leaguePoints])).toEqual([
      ['II', 50],
      ['II', 70],
      ['I', 70],
    ])
  })

  it('notes nothing while unranked or without a Solo/Duo standing', () => {
    recordRankSnapshot(riotId, null, new Date(T))
    recordRankSnapshot(riotId, standing(0, 'NONE', 'NA'), new Date(T))
    expect(readRankHistory(riotId)).toEqual([])
  })

  it('drops the entries past 90 days', () => {
    localStorage.setItem(`truemain:rank-history:${riotId}`, JSON.stringify([
      snapshot(T - 91 * DAY, 10),
      snapshot(T - 89 * DAY, 30),
    ]))
    const kept = recordRankSnapshot(riotId, standing(50), new Date(T))
    expect(kept.map(entry => entry.leaguePoints)).toEqual([30, 50])
    expect(readRankHistory(riotId)).toEqual(kept)
  })
})

describe('latestLpMoves', () => {
  // Solo/Duo games 40 minutes apart, the standing noted between each pair.
  const games = (count: number) => Array.from({ length: count }, (_, index) =>
    game({ gameId: index + 1, playedAt: T + index * 40 * MINUTE })).reverse()
  const historyOf = (lps: number[]) => lps.map((lp, index) => snapshot(T - MINUTE + index * 40 * MINUTE, lp))

  it('lists the measured games newest last', () => {
    expect(latestLpMoves(games(3), historyOf([50, 70, 55, 78]))).toEqual([
      { gameId: 1, delta: 20 },
      { gameId: 2, delta: -15 },
      { gameId: 3, delta: 23 },
    ])
  })

  it('keeps only the latest ones', () => {
    const moves = latestLpMoves(games(5), historyOf([10, 20, 30, 40, 50, 60]), 3)
    expect(moves.map(move => move.gameId)).toEqual([3, 4, 5])
  })

  it('skips a game it could not measure rather than guessing it', () => {
    const remake = game({ gameId: 9, playedAt: T + 40 * MINUTE, remake: true })
    const solo = [game({ gameId: 2, playedAt: T + 80 * MINUTE }), remake, game({ gameId: 1, playedAt: T })]
    const history = [snapshot(T - MINUTE, 50), snapshot(T + 32 * MINUTE, 70), snapshot(T + 79 * MINUTE, 70), snapshot(T + 112 * MINUTE, 92)]
    expect(latestLpMoves(solo, history)).toEqual([
      { gameId: 1, delta: 20 },
      { gameId: 2, delta: 22 },
    ])
  })

  it('is empty without a history', () => {
    expect(latestLpMoves(games(3), [])).toEqual([])
  })
})

describe('dailyStandings', () => {
  it('keeps the last standing of each day, in order', () => {
    const history = [snapshot(T, 10), snapshot(T + MINUTE, 30), snapshot(T + DAY, 50), snapshot(T + DAY + MINUTE, 40)]
    expect(dailyStandings(history).map(entry => entry.leaguePoints)).toEqual([30, 40])
  })

  it('is empty without a history', () => {
    expect(dailyStandings([])).toEqual([])
  })
})
