import { describe, expect, it } from 'vitest'
import type { WinProbabilityTimeline } from '#shared/types/win-probability'
import fixture from '#shared/fixtures/win-probability-timeline.json'
import {
  buildTimelineCurves,
  formatCurveDelta,
  formatCurveValue,
  formatTeamDifference,
  hasTimelineCurves,
  minuteTicks,
  niceCeiling,
  teamCurves,
} from '#common/utils/timeline-curves'

const timeline = fixture.timeline as WinProbabilityTimeline

function game(frames: WinProbabilityTimeline['frames']): WinProbabilityTimeline {
  return {
    durationMs: 120_000,
    participants: [
      { participantId: 1, teamId: 100, position: 'TOP' },
      { participantId: 2, teamId: 100, position: 'MIDDLE' },
      { participantId: 6, teamId: 200, position: 'TOP' },
    ],
    frames,
    events: [],
  }
}

const small = game([
  { ms: 0, players: [
    { participantId: 1, cs: 0, level: 1, gold: 500, xp: 0 },
    { participantId: 2, cs: 0, level: 1, gold: 500, xp: 0 },
    { participantId: 6, cs: 0, level: 1, gold: 500, xp: 0 },
  ] },
  { ms: 60_000, players: [
    { participantId: 1, cs: 4, level: 2, gold: 700, xp: 300 },
    { participantId: 6, cs: 6, level: 2, gold: 750, xp: 320 },
  ] },
  { ms: 110_000, players: [
    { participantId: 1, cs: 9, level: 3, gold: 1100, xp: 700 },
    { participantId: 2, cs: 8, level: 3, gold: 1000, xp: 680 },
    { participantId: 6, cs: 12, level: 3, gold: 1300, xp: 720 },
  ] },
])

describe('hasTimelineCurves', () => {
  it('needs two frames with gold on them', () => {
    expect(hasTimelineCurves(timeline)).toBe(true)
    expect(hasTimelineCurves(small)).toBe(true)
    expect(hasTimelineCurves(null)).toBe(false)
    expect(hasTimelineCurves(game([small.frames[0]!]))).toBe(false)
  })

  it('reads a timeline kept before the curves as none, gold missing or defaulted to zero', () => {
    const strip = (gold: number | undefined) => game(small.frames.map(frame => ({
      ...frame,
      players: frame.players.map(({ participantId, cs, level }) => ({ participantId, cs, level, ...(gold === undefined ? {} : { gold, xp: 0 }) })),
    })))
    expect(hasTimelineCurves(strip(undefined))).toBe(false)
    expect(hasTimelineCurves(strip(0))).toBe(false)
  })
})

describe('buildTimelineCurves', () => {
  it('draws one curve a player per metric, a missing frame holding the previous value', () => {
    const gold = buildTimelineCurves(small, 'gold', 100)!
    expect(gold.durationMs).toBe(120_000)
    expect(gold.players.map(p => p.participantId)).toEqual([1, 2, 6])
    expect(gold.players[1]!.points.map(p => p.value)).toEqual([500, 500, 1000])
    expect(gold.players[2]!.end).toBe(1300)
    expect(buildTimelineCurves(small, 'cs', 100)!.players[0]!.points.map(p => p.value)).toEqual([0, 4, 9])
    expect(buildTimelineCurves(small, 'xp', 100)!.players[0]!.end).toBe(700)
  })

  it('reads the lead as the perspective side minus the other', () => {
    expect(buildTimelineCurves(small, 'gold', 100)!.lead).toEqual([
      { ms: 0, value: 500 },
      { ms: 60_000, value: 450 },
      { ms: 110_000, value: 800 },
    ])
    expect(buildTimelineCurves(small, 'gold', 200)!.lead.map(p => p.value)).toEqual([-500, -450, -800])
  })

  it('spans the last frame when it runs past the reported duration', () => {
    expect(buildTimelineCurves({ ...small, durationMs: 100_000 }, 'gold', 100)!.durationMs).toBe(110_000)
  })

  it('sorts the frames by time', () => {
    const shuffled = { ...small, frames: [small.frames[2]!, small.frames[0]!, small.frames[1]!] }
    expect(buildTimelineCurves(shuffled, 'gold', 100)).toEqual(buildTimelineCurves(small, 'gold', 100))
  })

  it('gives the fixture game ten curves ending on the lead of the last frame', () => {
    const curves = buildTimelineCurves(timeline, 'gold', 100)!
    expect(curves.players).toHaveLength(10)
    const last = timeline.frames.at(-1)!.players
    const sum = (team: number) => last
      .filter(p => timeline.participants.find(q => q.participantId === p.participantId)!.teamId === team)
      .reduce((total, p) => total + p.gold!, 0)
    expect(curves.lead.at(-1)!.value).toBe(sum(100) - sum(200))
  })

  it('is null without curves', () => {
    expect(buildTimelineCurves(null, 'gold', 100)).toBeNull()
  })
})

describe('teamCurves', () => {
  it('puts the perspective side first, players by end value, with totals', () => {
    const [ours, theirs] = teamCurves(buildTimelineCurves(small, 'gold', 200)!, 200)
    expect(ours.teamId).toBe(200)
    expect(ours.total).toBe(1300)
    expect(theirs.teamId).toBe(100)
    expect(theirs.players.map(p => p.participantId)).toEqual([1, 2])
    expect(theirs.total).toBe(2100)
  })
})

describe('axis and labels', () => {
  it('rounds an axis top up to a round figure', () => {
    expect(niceCeiling(0)).toBe(1)
    expect(niceCeiling(12_017)).toBe(15_000)
    expect(niceCeiling(10_525)).toBe(12_000)
    expect(niceCeiling(2_400)).toBe(2_500)
    expect(niceCeiling(4_100)).toBe(5_000)
    expect(niceCeiling(258)).toBe(300)
    expect(niceCeiling(1_000)).toBe(1_000)
    expect(niceCeiling(9_100)).toBe(10_000)
  })

  it('prints gold and XP in thousands, CS as a count', () => {
    expect(formatCurveValue(12_017, 'gold')).toBe('12.0k')
    expect(formatCurveValue(731, 'xp')).toBe('731')
    expect(formatCurveValue(1_204, 'cs')).toBe('1,204')
    expect(formatCurveDelta(4_321, 'gold')).toBe('+4.3k')
    expect(formatCurveDelta(-12, 'cs')).toBe('−12')
    expect(formatCurveDelta(0.2, 'cs')).toBe('0')
  })

  it('says the end-of-game difference from the viewer\'s side', () => {
    expect(formatTeamDifference(4_321, 'gold')).toBe('Your team +4.3k gold')
    expect(formatTeamDifference(-12, 'cs')).toBe('Your team −12 CS')
    expect(formatTeamDifference(0, 'xp')).toBe('Teams even on XP')
  })

  it('labels every 5 minutes, every 10 past half an hour', () => {
    expect(minuteTicks(1_842_000).map(t => t.minute)).toEqual([0, 5, 10, 15, 20, 25, 30])
    expect(minuteTicks(41 * 60_000).map(t => t.minute)).toEqual([0, 10, 20, 30, 40])
    expect(minuteTicks(17 * 60_000).map(t => t.minute)).toEqual([0, 5, 10, 15])
    expect(minuteTicks(10 * 60_000)[2]).toEqual({ minute: 10, ms: 600_000 })
  })
})
