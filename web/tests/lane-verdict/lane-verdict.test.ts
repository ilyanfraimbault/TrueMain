import { describe, expect, it } from 'vitest'
import {
  formatGoldDiff,
  goldDiffBand,
  laneVerdict,
  LANE_DOMINANT_GOLD,
  LANE_EVEN_GOLD,
  LANE_VERDICT_MIN_GAMES,
} from '~/utils/lane-verdict'

const SAMPLE = LANE_VERDICT_MIN_GAMES

describe('laneVerdict', () => {
  it('bands the gold gap into the five verdicts', () => {
    expect(laneVerdict(600, SAMPLE)?.label).toBe('Very good lane')
    expect(laneVerdict(200, SAMPLE)?.label).toBe('Good lane')
    expect(laneVerdict(40, SAMPLE)?.label).toBe('Even lane')
    expect(laneVerdict(-200, SAMPLE)?.label).toBe('Bad lane')
    expect(laneVerdict(-600, SAMPLE)?.label).toBe('Hard lane')
  })

  it('puts each boundary in the stronger band, symmetrically', () => {
    // The edges are the whole contract of a banding function: 150 must not read
    // as even on one side and good on the other.
    expect(laneVerdict(LANE_EVEN_GOLD, SAMPLE)?.label).toBe('Good lane')
    expect(laneVerdict(LANE_EVEN_GOLD - 1, SAMPLE)?.label).toBe('Even lane')
    expect(laneVerdict(-LANE_EVEN_GOLD, SAMPLE)?.label).toBe('Bad lane')
    expect(laneVerdict(-(LANE_EVEN_GOLD - 1), SAMPLE)?.label).toBe('Even lane')
    expect(laneVerdict(LANE_DOMINANT_GOLD, SAMPLE)?.label).toBe('Very good lane')
    expect(laneVerdict(LANE_DOMINANT_GOLD - 1, SAMPLE)?.label).toBe('Good lane')
    expect(laneVerdict(-LANE_DOMINANT_GOLD, SAMPLE)?.label).toBe('Hard lane')
  })

  it('withholds a verdict when there is nothing to call', () => {
    // An unmeasured gap and a gap measured on three lanes are both "we do not
    // know" — neither may become "Even lane", the most confident-looking answer.
    expect(laneVerdict(null, 500)).toBeNull()
    expect(laneVerdict(0, SAMPLE - 1)).toBeNull()
    expect(laneVerdict(900, 3)).toBeNull()
    expect(laneVerdict(0, SAMPLE)?.label).toBe('Even lane')
  })

  it('says matchup, not lane, where there is no lane', () => {
    expect(laneVerdict(600, SAMPLE, 'matchup')?.label).toBe('Very good matchup')
    expect(laneVerdict(0, SAMPLE, 'matchup')?.label).toBe('Even matchup')
  })
})

describe('formatGoldDiff', () => {
  it('always carries the sign of the side it favours', () => {
    expect(formatGoldDiff(312)).toBe('+312')
    expect(formatGoldDiff(-184.4)).toBe('−184')
    expect(formatGoldDiff(0)).toBe('0')
    // A gap that rounds away has no side to favour — and must not print "−0".
    expect(formatGoldDiff(-0.4)).toBe('0')
    expect(formatGoldDiff(1240)).toBe('+1,240')
  })
})

describe('goldDiffBand', () => {
  it('reads the same bands as the tone, on the same boundaries', () => {
    expect(goldDiffBand(LANE_EVEN_GOLD)).toBe('good')
    expect(goldDiffBand(LANE_EVEN_GOLD - 1)).toBe('mid')
    expect(goldDiffBand(-(LANE_EVEN_GOLD - 1))).toBe('mid')
    expect(goldDiffBand(-LANE_EVEN_GOLD)).toBe('bad')
  })

  it('separates a measured draw from a gap nobody measured', () => {
    // `mid` is "we measured it and it is even"; `default` is "there is no such
    // reading". Collapsing them prints the most decisive-looking verdict there
    // is — dead even — out of data that does not exist (#976).
    expect(goldDiffBand(0)).toBe('mid')
    expect(goldDiffBand(null)).toBe('default')
    expect(goldDiffBand(undefined)).toBe('default')
  })
})
