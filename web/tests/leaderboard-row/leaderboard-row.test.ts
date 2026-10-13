import { describe, expect, it } from 'vitest'
import type { LeaderboardTopChampion } from '#shared/types/leaderboard'
import { EMPTY_STAT, isOneTrick, leaderboardLanes, leaderboardStatLabels, subChampionSlots } from '#common/utils/leaderboard-row'

const champion = (championId: number, isOtp = false): LeaderboardTopChampion => ({
  championId,
  games: 10,
  playRate: 0.5,
  isOtp,
  primaryKeystoneId: null,
  secondaryStyleId: null,
  firstItemId: null,
})

describe('leaderboardLanes', () => {
  it('lists the primary lane, then the secondary one', () => {
    const lanes = leaderboardLanes({ primary: 'MIDDLE', secondary: 'TOP' })
    expect(lanes.map(lane => [lane.position, lane.primary])).toEqual([['MIDDLE', true], ['TOP', false]])
    expect(lanes[0]?.title).toMatch(/^Primary: /)
    expect(lanes[1]?.title).toMatch(/^Secondary: /)
  })

  it('is empty without position data', () => {
    expect(leaderboardLanes(null)).toEqual([])
    expect(leaderboardLanes({ primary: 'JUNGLE', secondary: null })).toHaveLength(1)
  })
})

describe('isOneTrick', () => {
  it('flags the player when any top champion is an OTP', () => {
    expect(isOneTrick([champion(1), champion(2, true)])).toBe(true)
    expect(isOneTrick([champion(1)])).toBe(false)
  })
})

describe('subChampionSlots', () => {
  it('always holds two slots after the signature champion', () => {
    expect(subChampionSlots([champion(1)])).toEqual([null, null])
    expect(subChampionSlots([champion(1), champion(2)]).map(slot => slot?.championId ?? null)).toEqual([2, null])
    expect(subChampionSlots([champion(1), champion(2), champion(3)]).map(slot => slot?.championId)).toEqual([2, 3])
  })
})

describe('leaderboardStatLabels', () => {
  it('writes a missing figure as an em dash', () => {
    expect(leaderboardStatLabels({ games: 1204, wins: null, losses: null, winRate: null, kda: null }))
      .toEqual({ games: '1,204', kda: EMPTY_STAT, winRate: EMPTY_STAT })
  })

  it('rounds the KDA to a tenth and the win rate to a percent', () => {
    const labels = leaderboardStatLabels({ games: 3, wins: 2, losses: 1, winRate: 2 / 3, kda: 3.456 })
    expect(labels.kda).toBe('3.5')
    expect(labels.winRate).toBe('67%')
  })
})
