import { describe, expect, it } from 'vitest'
import { MATCHUP_LEADERBOARD_SIZE, rankMatchups } from '~~/shared/utils/matchup-ranking'

/**
 * The matchup leaderboard's order (#1087), shared by the Matchups panel and the
 * server-rendered matchup sentences (#1954). What it must never do: rank a
 * small sample's extreme rate above a large sample's solid one, or list the same
 * opponent as both a best and a worst matchup.
 */

function entry(opponentChampionId: number, winRateLowerBound: number, winRateUpperBound: number) {
  return { opponentChampionId, winRateLowerBound, winRateUpperBound }
}

describe('rankMatchups', () => {
  it('ranks best on the lower bound, not on the widest interval\'s top', () => {
    // 11 games at 82% bounds around [0.52, 0.95]; 739 games at 57% around [0.54, 0.61].
    const thin = entry(1, 0.52, 0.95)
    const solid = entry(2, 0.54, 0.61)

    expect(rankMatchups([thin, solid], 1).best).toEqual([solid])
  })

  it('ranks worst on the upper bound ascending', () => {
    const thinLoss = entry(1, 0.05, 0.48)
    const solidLoss = entry(2, 0.40, 0.46)
    const even = entry(3, 0.45, 0.55)

    expect(rankMatchups([thinLoss, solidLoss, even], 1).worst).toEqual([solidLoss])
  })

  it('never lists an opponent on both sides', () => {
    const entries = [entry(1, 0.6, 0.7), entry(2, 0.5, 0.6), entry(3, 0.4, 0.5)]

    const { best, worst } = rankMatchups(entries, 2)

    expect(best.map(m => m.opponentChampionId)).toEqual([1, 2])
    expect(worst.map(m => m.opponentChampionId)).toEqual([3])
  })

  it('defaults to the panel\'s five per side', () => {
    const entries = Array.from({ length: 20 }, (_, i) => entry(i + 1, i / 40, i / 40 + 0.1))

    const { best, worst } = rankMatchups(entries)

    expect(best).toHaveLength(MATCHUP_LEADERBOARD_SIZE)
    expect(worst).toHaveLength(MATCHUP_LEADERBOARD_SIZE)
  })

  it('leaves its input in the order it came', () => {
    const entries = [entry(1, 0.4, 0.5), entry(2, 0.6, 0.7)]

    rankMatchups(entries)

    expect(entries.map(m => m.opponentChampionId)).toEqual([1, 2])
  })
})
