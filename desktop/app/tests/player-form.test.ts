import { describe, expect, it } from 'vitest'
import type { MetricDefinition } from '~/utils/player-form'
import { METRICS, championLines, laneLines, readMetric } from '~/utils/player-form'
import { game } from './fixtures'

const metric = (key: string): MetricDefinition => METRICS.find(entry => entry.key === key)!
const deaths = metric('deaths')
const kda = metric('kda')

/** Newest first: `recent` games, then `older` ones, each with the deaths given. */
function sample(recentDeaths: number[], olderDeaths: number[]) {
  return [...recentDeaths, ...olderDeaths].map((value, index) => game({ gameId: index + 1, deaths: value }))
}

describe('readMetric', () => {
  it('leaves remakes out of the value, the delta and the series', () => {
    const games = sample([2, 2, 2, 2, 2], [6, 6, 6, 6, 6])
    const withRemakes = [game({ gameId: 100, deaths: 40, remake: true }), ...games, game({ gameId: 101, deaths: 40, remake: true })]
    expect(readMetric(deaths, withRemakes)).toEqual(readMetric(deaths, games))
    expect(readMetric(deaths, withRemakes).series).toHaveLength(10)
  })

  it('gives no delta on a sample too short to split, and reads it whole', () => {
    // Seven counted games: the remake does not make up the eighth.
    const games = [...sample([2, 2, 2, 2, 2], [9, 9]), game({ gameId: 50, remake: true })]
    const reading = readMetric(deaths, games)
    expect(reading.delta).toBeNull()
    expect(reading.improved).toBeNull()
    expect(reading.value).toBe(4)
  })

  it('reads the latest games against the whole sample once it is long enough', () => {
    const reading = readMetric(deaths, sample([2, 2, 2, 2, 2], [6, 6, 6]))
    expect(reading.value).toBe(2)
    expect(reading.delta).toBeCloseTo(2 - 28 / 8)
  })

  it('gives no verdict on a flat move', () => {
    expect(readMetric(deaths, sample([5, 5, 5, 5, 5], [5, 5, 5, 5, 5]))).toMatchObject({ delta: 0, improved: null })
    // 4.9 against an average of 4.95: a 1% move is noise.
    const reading = readMetric(deaths, sample([5, 5, 5, 5, 4.5], [5, 5, 5, 5, 5]))
    expect(reading.delta).not.toBe(0)
    expect(reading.improved).toBeNull()
  })

  it('reads fewer deaths as the better direction', () => {
    expect(readMetric(deaths, sample([2, 2, 2, 2, 2], [6, 6, 6, 6, 6])).improved).toBe(true)
    expect(readMetric(deaths, sample([6, 6, 6, 6, 6], [2, 2, 2, 2, 2])).improved).toBe(false)
  })

  it('reads a higher value as better on the other metrics', () => {
    // Fewer deaths is a higher KDA: the same games read the other way round.
    expect(readMetric(kda, sample([2, 2, 2, 2, 2], [6, 6, 6, 6, 6])).improved).toBe(true)
    expect(readMetric(kda, sample([6, 6, 6, 6, 6], [2, 2, 2, 2, 2])).improved).toBe(false)
  })
})

describe('championLines', () => {
  const games = [
    game({ gameId: 1, championId: 7, position: 'MIDDLE', win: true, kills: 10, deaths: 2, assists: 4 }),
    game({ gameId: 2, championId: 7, position: 'TOP', win: false, kills: 1, deaths: 6, assists: 3 }),
    game({ gameId: 3, championId: 7, position: 'MIDDLE', win: true, kills: 4, deaths: 1, assists: 8 }),
    game({ gameId: 4, championId: 3, position: null, win: true, kills: 2, deaths: 2, assists: 2, queueId: 450 }),
    game({ gameId: 5, championId: 7, position: 'TOP', win: false, remake: true }),
    game({ gameId: 6, championId: 9, position: 'JUNGLE', win: false, remake: true }),
  ]

  it('counts games, wins and the KDA line per champion, remakes aside, most played first', () => {
    expect(championLines(games)).toEqual([
      { championId: 7, position: 'MIDDLE', games: 3, wins: 2, kills: 15, deaths: 9, assists: 15 },
      { championId: 3, position: null, games: 1, wins: 1, kills: 2, deaths: 2, assists: 2 },
    ])
  })

  it('orders champions on as many games by wins', () => {
    const lines = championLines([
      game({ gameId: 1, championId: 1, win: false }),
      game({ gameId: 2, championId: 2, win: true }),
    ])
    expect(lines.map(line => line.championId)).toEqual([2, 1])
  })
})

describe('laneLines', () => {
  it('counts games and wins on every lane, in lane order, unplayed lanes at zero', () => {
    const games = [
      game({ gameId: 1, position: 'MIDDLE', win: true }),
      game({ gameId: 2, position: 'MIDDLE', win: false }),
      game({ gameId: 3, position: 'UTILITY', win: true }),
      game({ gameId: 4, position: 'TOP', win: true, remake: true }),
      game({ gameId: 5, position: null, win: true, queueId: 450 }),
    ]
    expect(laneLines(games)).toEqual([
      { position: 'TOP', games: 0, wins: 0 },
      { position: 'JUNGLE', games: 0, wins: 0 },
      { position: 'MIDDLE', games: 2, wins: 1 },
      { position: 'BOTTOM', games: 0, wins: 0 },
      { position: 'UTILITY', games: 1, wins: 1 },
    ])
  })
})
