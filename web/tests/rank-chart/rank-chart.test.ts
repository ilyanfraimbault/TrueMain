import { describe, expect, it } from 'vitest'
import type { RankHistoryEntry } from '#shared/types/rank-history'
import { lpDeltaSince, rankChartPoints } from '#common/utils/rank-chart'

const NOW = Date.parse('2026-10-13T12:00:00Z')
const DAY = 24 * 60 * 60 * 1000

function snapshot(daysAgo: number, tier: string, division: string, leaguePoints: number): RankHistoryEntry {
  return { capturedAtUtc: new Date(NOW - daysAgo * DAY).toISOString(), tier, division, leaguePoints }
}

const climb = rankChartPoints([
  snapshot(40, 'GOLD', 'I', 80),
  snapshot(20, 'PLATINUM', 'IV', 10),
  snapshot(5, 'PLATINUM', 'IV', 60),
  snapshot(1, 'PLATINUM', 'III', 20),
])

describe('lpDeltaSince', () => {
  it('compares the latest score to the last snapshot before the cutoff', () => {
    expect(lpDeltaSince(climb, 7, NOW)).toBe(climb[3]!.score - climb[1]!.score)
    expect(lpDeltaSince(climb, 30, NOW)).toBe(climb[3]!.score - climb[0]!.score)
  })

  it('falls back to the earliest snapshot when none is older than the cutoff', () => {
    expect(lpDeltaSince(climb.slice(2), 30, NOW)).toBe(climb[3]!.score - climb[2]!.score)
  })

  it('has nothing to compare with a single snapshot', () => {
    expect(lpDeltaSince(climb.slice(0, 1), 7, NOW)).toBeNull()
  })
})
