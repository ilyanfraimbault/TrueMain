import { describe, expect, it } from 'vitest'
import { formatPlayTime, todayOf } from '~/utils/today'
import { MINUTE, game, snapshot } from './fixtures'

// Local times: "today" is the player's own calendar day.
const NOW = new Date(2026, 9, 16, 21, 0)
const at = (hours: number, minutes = 0) => new Date(2026, 9, 16, hours, minutes).getTime()

describe('todayOf', () => {
  it('is null without a game since local midnight', () => {
    const yesterday = game({ playedAt: new Date(2026, 9, 15, 23, 30).getTime() })
    expect(todayOf([yesterday], [yesterday], [], NOW)).toBeNull()
  })

  it('counts the games started today, their record and the time spent in them', () => {
    const games = [
      game({ gameId: 3, playedAt: at(20), win: false, durationSeconds: 1500 }),
      game({ gameId: 2, playedAt: at(19), win: true, durationSeconds: 1800, queueId: 450 }),
      game({ gameId: 1, playedAt: new Date(2026, 9, 15, 22).getTime() }),
    ]
    const summary = todayOf(games, games, [], NOW)
    expect(summary).toMatchObject({ games: 2, wins: 1, losses: 1, seconds: 3300, soloGames: 1 })
  })

  it('leaves remakes out', () => {
    const games = [game({ gameId: 2, playedAt: at(20), remake: true }), game({ gameId: 1, playedAt: at(19) })]
    expect(todayOf(games, games, [], NOW)?.games).toBe(1)
  })

  it('sums the measured LP of the day and says how many Solo/Duo games it covers', () => {
    const games = [
      game({ gameId: 3, playedAt: at(20) }),
      game({ gameId: 2, playedAt: at(19) }),
      game({ gameId: 1, playedAt: at(18) }),
    ]
    // Game 1 has no snapshot before it; games 2 and 3 are measured: +20, then −15.
    const history = [
      snapshot(at(18, 31), 40),
      snapshot(at(19, 31), 60),
      snapshot(at(20, 31), 45),
    ]
    expect(todayOf(games, games, history, NOW)).toMatchObject({ lp: 5, lpGames: 2, soloGames: 3 })
  })

  it('has no LP figure when no Solo/Duo game of the day is measured', () => {
    const games = [game({ playedAt: at(20) })]
    expect(todayOf(games, games, [snapshot(at(20) - MINUTE, 50)], NOW)).toMatchObject({ lp: null, lpGames: 0 })
  })
})

describe('formatPlayTime', () => {
  it('writes hours and minutes', () => {
    expect(formatPlayTime(30)).toBe('<1m')
    expect(formatPlayTime(38 * 60 + 20)).toBe('38m')
    expect(formatPlayTime(112 * 60)).toBe('1h 52m')
    expect(formatPlayTime(125 * 60)).toBe('2h 05m')
  })
})
