import { describe, expect, it } from 'vitest'
import type { DraftReason } from '~/types/draft'
import { describeReason, draftPercent, draftPoints } from '~/utils/draft-reasons'

const names: Record<number, string> = { 238: 'Zed', 117: 'Lulu', 103: 'Ahri', 105: 'Fizz' }
const nameOf = (id: number) => names[id] ?? `#${id}`

function reason(fields: Partial<DraftReason>): DraftReason {
  return {
    kind: 'laneMatchup',
    championId: null,
    position: null,
    delta: null,
    games: 0,
    rate: null,
    probability: null,
    share: null,
    count: null,
    of: null,
    tentative: false,
    ...fields,
  }
}

describe('draftPoints', () => {
  it('signs a delta in win-rate points', () => {
    expect(draftPoints(0.031)).toBe('+3.1 pts')
    expect(draftPoints(-0.042)).toBe('−4.2 pts')
  })
})

describe('draftPercent', () => {
  it('keeps a decimal only for small rates', () => {
    expect(draftPercent(0.58)).toBe('58%')
    expect(draftPercent(0.092)).toBe('9.2%')
  })
})

describe('describeReason', () => {
  it('names the lane opponent and says when the lane is only a guess', () => {
    const sure = describeReason(reason({ championId: 238, delta: 0.031, games: 1240, probability: 1 }), nameOf, 'MIDDLE')
    expect(sure.short).toBe('vs Zed')
    expect(sure.full).toBe('+3.1 pts vs Zed over 1,240 games')
    expect(sure.tone).toBe('good')

    const guess = describeReason(reason({ championId: 238, delta: -0.02, games: 300, probability: 0.6 }), nameOf, 'MIDDLE')
    expect(guess.short).toBe('vs Zed (likely)')
    expect(guess.full).toContain('60% sure Zed plays your lane')
    expect(guess.tone).toBe('bad')
  })

  it('reads blind safety with its lower tail', () => {
    const safe = describeReason(reason({ kind: 'blindSafety', delta: 0.012, games: 9000, count: 0, of: 8 }), nameOf, 'MIDDLE')
    expect(safe.short).toBe('Safe blind')
    expect(safe.full).toContain('the Mid opponents still open')
    const risky = describeReason(reason({ kind: 'blindSafety', delta: 0.004, games: 9000, count: 2, of: 8 }), nameOf, 'MIDDLE')
    expect(risky.short).toBe('Behind 2/8 blind')
    expect(risky.tone).toBe('bad')
  })

  it('flags a synergy that rests on a hover', () => {
    const text = describeReason(reason({ kind: 'synergy', championId: 117, delta: 0.018, games: 410, tentative: true }), nameOf, 'MIDDLE')
    expect(text.short).toBe('With Lulu (hover)')
    expect(text.full).toBe('+1.8 pts with Lulu, who is only hovering over 410 games')
  })

  it('words a ban threat against our pick and its lane share', () => {
    const text = describeReason(reason({ kind: 'laneThreat', championId: 103, delta: -0.042, games: 2100, share: 0.09 }), nameOf, 'MIDDLE')
    expect(text.headline).toBe('−4.2')
    expect(text.unit).toBe('pts')
    expect(text.short).toBe('Into your Ahri')
    expect(text.full).toBe('Your Ahri: −4.2 pts into it over 2,100 games, which is played in 9.0% of Mid games')
  })

  it('labels the ban-rate fallback as such, not as a threat', () => {
    const text = describeReason(reason({ kind: 'banRate', championId: 105, rate: 0.12, games: 50000 }), nameOf, 'MIDDLE')
    expect(text.short).toBe('Banned in 12%')
    expect(text.full).toContain('not a threat measured against you')
    expect(text.tone).toBe('neutral')
  })

  it('reads a lane-phase rate', () => {
    const text = describeReason(reason({ kind: 'lanePhase', championId: 238, rate: 0.58, games: 900 }), nameOf, 'MIDDLE')
    expect(text.headline).toBe('58')
    expect(text.short).toBe('Wins lane vs Zed')
  })

  it('still shows the figure of a kind it does not know', () => {
    const text = describeReason(reason({ kind: 'teamComp', delta: 0.01 }), nameOf, 'MIDDLE')
    expect(text.headline).toBe('+1.0')
  })
})
