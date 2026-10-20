import { describe, expect, it } from 'vitest'
import type { ApexCutoff } from '#shared/types/apex-cutoffs'
import type { RankHistoryEntry } from '#shared/types/rank-history'
import { rankChartPoints } from '#common/utils/rank-chart'
import {
  apexFloorsAt,
  MASTER_FLOOR,
  rankChartYDomain,
  tierBoundaries,
  tierZones,
} from '#common/utils/rank-chart-zones'

const DAY = 24 * 60 * 60 * 1000
const START = Date.parse('2026-09-01T12:00:00Z')

function snapshot(day: number, tier: string, division: string, leaguePoints: number): RankHistoryEntry {
  return { capturedAtUtc: new Date(START + day * DAY).toISOString(), tier, division, leaguePoints }
}

function cutoff(day: number, gm: number | null, ch: number | null): ApexCutoff {
  return { dateUtc: new Date(START + day * DAY).toISOString().slice(0, 10), grandmasterMinLp: gm, challengerMinLp: ch }
}

const apex = rankChartPoints([
  snapshot(0, 'MASTER', 'I', 300),
  snapshot(1, 'GRANDMASTER', 'I', 420),
  snapshot(2, 'GRANDMASTER', 'I', 460),
  snapshot(6, 'MASTER', 'I', 350),
])

describe('apexFloorsAt', () => {
  it('reads the cutoff of the snapshot\'s UTC day as a rank score', () => {
    const floors = apexFloorsAt(apex, [cutoff(0, 400, 900), cutoff(1, 410, 905), cutoff(2, 415, 910)])
    expect(floors[0]).toMatchObject({ grandmaster: MASTER_FLOOR + 400, challenger: MASTER_FLOOR + 900 })
    expect(floors[2]).toMatchObject({ grandmaster: MASTER_FLOOR + 415, challenger: MASTER_FLOOR + 910 })
  })

  it('holds the latest earlier cutoff for two days, then gives up', () => {
    const floors = apexFloorsAt(apex, [cutoff(0, 400, 900)])
    expect(floors[2]!.grandmaster).toBe(MASTER_FLOOR + 400)
    expect(floors[3]).toMatchObject({ grandmaster: null, challenger: null })
  })

  it('knows nothing before the first measurement', () => {
    expect(apexFloorsAt(apex, [cutoff(1, 400, 900)])[0]).toMatchObject({ grandmaster: null, challenger: null })
  })

  it('keeps one tier when only the other was measured', () => {
    expect(apexFloorsAt(apex, [cutoff(0, null, 900)])[0]).toMatchObject({ grandmaster: null, challenger: MASTER_FLOOR + 900 })
  })
})

describe('tierBoundaries', () => {
  it('starts every tier below Master at a fixed score', () => {
    const boundaries = tierBoundaries(apex, apexFloorsAt(apex, []))
    expect(boundaries.map(b => b.tier)).toEqual([
      'BRONZE', 'SILVER', 'GOLD', 'PLATINUM', 'EMERALD', 'DIAMOND', 'MASTER', 'GRANDMASTER', 'CHALLENGER',
    ])
    expect(boundaries.find(b => b.tier === 'DIAMOND')!.scores).toEqual([2400, 2400, 2400, 2400])
    expect(boundaries.find(b => b.tier === 'MASTER')!.measured.every(Boolean)).toBe(true)
  })

  it('falls back to the nearest cutoff, however far, before the snapshot\'s tier', () => {
    // Only the last snapshot's day was measured: every earlier one takes that cutoff for its
    // band, so the curve splits at the line rather than at the snapshot whose tier changed.
    const floors = apexFloorsAt(apex, [cutoff(6, 380, 900)])
    expect(floors[0]).toMatchObject({ grandmaster: null, nearestGrandmaster: MASTER_FLOOR + 380 })
    const gm = tierBoundaries(apex, floors).find(b => b.tier === 'GRANDMASTER')!
    expect(gm.scores).toEqual([MASTER_FLOOR + 380, MASTER_FLOOR + 380, MASTER_FLOOR + 380, MASTER_FLOOR + 380])
    expect(gm.measured).toEqual([false, false, false, true])
  })

  it('reads an unmeasured apex floor back from the snapshot\'s own tier', () => {
    const gm = tierBoundaries(apex, apexFloorsAt(apex, [])).find(b => b.tier === 'GRANDMASTER')!
    expect(gm.scores).toEqual([Infinity, MASTER_FLOOR, MASTER_FLOOR, Infinity])
    expect(gm.measured).toEqual([false, false, false, false])
  })

  it('never puts Challenger under Grandmaster', () => {
    const boundaries = tierBoundaries(apex, apexFloorsAt(apex, [cutoff(0, 400, 900)]))
    const ch = boundaries.find(b => b.tier === 'CHALLENGER')!
    const gm = boundaries.find(b => b.tier === 'GRANDMASTER')!
    ch.scores.forEach((score, i) => expect(score).toBeGreaterThanOrEqual(gm.scores[i]!))
  })
})

describe('tierZones', () => {
  it('bands every tier between its own boundary and the next', () => {
    const zones = tierZones(tierBoundaries(apex, apexFloorsAt(apex, [])))
    expect(zones[0]).toMatchObject({ tier: 'IRON', lower: null })
    expect(zones[0]!.upper!.tier).toBe('BRONZE')
    expect(zones.at(-1)).toMatchObject({ tier: 'CHALLENGER', upper: null })
  })
})

describe('rankChartYDomain', () => {
  const master = rankChartPoints([snapshot(0, 'MASTER', 'I', 300), snapshot(1, 'MASTER', 'I', 320)])

  it('pads the range so the line never hugs an edge', () => {
    const [min, max] = rankChartYDomain(master)
    expect(min).toBeLessThan(MASTER_FLOOR + 300)
    expect(max).toBeGreaterThan(MASTER_FLOOR + 320)
    expect(rankChartYDomain([])).toEqual([0, 400])
  })

  it('stretches to a measured threshold just above the peak', () => {
    const boundaries = tierBoundaries(master, apexFloorsAt(master, [cutoff(0, 440, 900), cutoff(1, 440, 900)]))
    expect(rankChartYDomain(master, boundaries)[1]).toBe(MASTER_FLOOR + 440 + 25)
  })

  it('leaves a far threshold out of view', () => {
    const boundaries = tierBoundaries(master, apexFloorsAt(master, [cutoff(0, 700, 900)]))
    expect(rankChartYDomain(master, boundaries)).toEqual(rankChartYDomain(master))
  })
})
