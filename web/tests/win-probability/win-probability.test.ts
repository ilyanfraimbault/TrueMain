import { describe, expect, it } from 'vitest'
import type { MatchWinProbability, WinProbabilityTimeline } from '#shared/types/win-probability'
import fixture from '#shared/fixtures/win-probability-timeline.json'
import { MAP_WEIGHTS, weightAt, winProbability, type SideMap } from '#common/utils/win-probability'
import { buildWinProbability, WIN_PROBABILITY_SWING_LIMIT } from '#common/utils/win-probability-timeline'

const timeline = fixture.timeline as WinProbabilityTimeline
const expected = fixture.expected as MatchWinProbability
const even: SideMap = { turrets: 0, inhibitorsDown: 0, dragons: 0, baron: false, elder: false }
const logistic = (x: number) => 1 / (1 + Math.exp(-x))

describe('winProbability', () => {
  it('is even when nothing separates the sides', () => {
    expect(winProbability({ leads: [], ours: even, theirs: even, clock: 600 })).toBe(0.5)
  })

  it('weighs the map in log-odds', () => {
    const ours = { ...even, turrets: 3, baron: true }
    expect(winProbability({ leads: [], ours, theirs: even, clock: 1500 }))
      .toBeCloseTo(logistic(3 * MAP_WEIGHTS.turret + MAP_WEIGHTS.baron), 12)
  })

  it('interpolates the lead weights between the marks and clamps outside them', () => {
    expect(weightAt([1, 2, 3, 4, 5], 0)).toBe(1)
    expect(weightAt([1, 2, 3, 4, 5], 12.5 * 60)).toBe(2.5)
    expect(weightAt([1, 2, 3, 4, 5], 25 * 60)).toBe(4.5)
    expect(weightAt([1, 2, 3, 4, 5], 45 * 60)).toBe(5)
  })
})

describe('buildWinProbability', () => {
  // The fixture is shared with the ingestor's port (WinProbabilityParityTests):
  // a figure changed here without the C# side fails there, and the reverse.
  it('matches the shared fixture', () => {
    const built = buildWinProbability(timeline)!
    expect(built.points.length).toBe(expected.points.length)
    built.points.forEach((point, i) => {
      expect(point.ms).toBe(expected.points[i]!.ms)
      expect(point.p).toBeCloseTo(expected.points[i]!.p, 9)
    })
    expect(built.swings.map(s => ({ ...s, delta: Math.round(s.delta * 1e9) })))
      .toEqual(expected.swings.map(s => ({ ...s, delta: Math.round(s.delta * 1e9) })))
    expect(built.objectives.map(o => ({ ...o, delta: o.delta === null ? null : Math.round(o.delta * 1e9) })))
      .toEqual(expected.objectives.map(o => ({ ...o, delta: o.delta === null ? null : Math.round(o.delta * 1e9) })))
  })

  it('starts even, ends at the game\'s end and keeps the largest swings first', () => {
    const built = buildWinProbability(timeline)!
    expect(built.points[0]).toEqual({ ms: 0, p: 0.5 })
    expect(built.points.at(-1)!.ms).toBe(timeline.durationMs)
    expect(built.swings.length).toBe(WIN_PROBABILITY_SWING_LIMIT)
    const sizes = built.swings.map(s => Math.abs(s.delta))
    expect(sizes).toEqual([...sizes].sort((a, b) => b - a))
  })

  it('weighs a turret as exactly its map weight at that moment', () => {
    const lone: WinProbabilityTimeline = {
      ...timeline,
      events: [{ type: 'BUILDING_KILL', ms: 600_000, teamId: 200, buildingType: 'TOWER_BUILDING', laneType: 'BOT_LANE', towerType: 'OUTER_TURRET' }],
    }
    const built = buildWinProbability(lone)!
    const [swing] = built.swings
    expect(swing).toMatchObject({ kind: 'turret', teamId: 100, lane: 'BOT_LANE', towerType: 'OUTER_TURRET' })
    expect(swing!.delta).toBeGreaterThan(0)
  })

  it('gives the model\'s unweighted monsters no delta, and an execution no swing', () => {
    const built = buildWinProbability(timeline)!
    const unweighted = built.objectives.filter(o => ['HORDE', 'RIFTHERALD', 'ATAKHAN'].includes(o.monsterType))
    expect(unweighted.length).toBe(3)
    expect(unweighted.every(o => o.delta === null)).toBe(true)
    expect(built.swings.some(s => s.kind === 'kill' && s.killerId === 0)).toBe(false)
  })

  it('credits an epic monster to its killer\'s side when the timeline names no team', () => {
    const elder = buildWinProbability(timeline)!.objectives.find(o => o.monsterSubType === 'ELDER_DRAGON')!
    expect(elder.teamId).toBe(200)
    expect(elder.delta).toBeLessThan(0)
  })

  it('draws no curve for a short game or without the five lanes', () => {
    expect(buildWinProbability({ ...timeline, durationMs: 14 * 60_000 })).toBeNull()
    const noLanes = timeline.participants.map(p => ({ ...p, position: '' }))
    expect(buildWinProbability({ ...timeline, participants: noLanes })).toBeNull()
  })
})
