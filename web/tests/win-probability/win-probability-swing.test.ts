import { describe, expect, it } from 'vitest'
import type { WinProbabilitySwing } from '#shared/types/win-probability'
import {
  describeObjective,
  describeSwing,
  formatGameClock,
  formatSwingPoints,
  forTeam,
  resolveWinProbability,
} from '#common/utils/win-probability-swing'

const names: Record<number, string> = { 3: 'Ahri', 8: 'Zed' }
const context = { perspectiveTeamId: 100, championName: (id: number) => names[id] ?? '?' }
const swing = (overrides: Partial<WinProbabilitySwing>): WinProbabilitySwing => ({
  ms: 0, kind: 'kill', teamId: 100, delta: 0.05, killerId: 0, victimId: 0, assists: 0,
  bounty: null, lane: null, towerType: null, monsterSubType: null, ...overrides,
})

describe('describeSwing', () => {
  it('reads a kill from the viewer\'s side, with its assists and gold', () => {
    const ours = describeSwing(swing({ killerId: 3, victimId: 8, assists: 2, bounty: 300 }), context)
    expect(ours).toEqual({ label: 'Your Ahri killed Zed (+2 assists)', ours: true, delta: 0.05, gold: 300 })
    const theirs = describeSwing(swing({ teamId: 200, killerId: 8, victimId: 3, delta: -0.04 }), context)
    expect(theirs).toMatchObject({ label: 'Enemy Zed killed Ahri', ours: false, delta: -0.04 })
  })

  it('turns the delta to red side', () => {
    expect(describeSwing(swing({ delta: 0.05 }), { ...context, perspectiveTeamId: 200 }).delta).toBe(-0.05)
  })

  it('names buildings and monsters', () => {
    expect(describeSwing(swing({ kind: 'turret', teamId: 200, lane: 'MID_LANE', towerType: 'INNER_TURRET' }), context).label)
      .toBe('Lost the inner turret, mid')
    expect(describeSwing(swing({ kind: 'inhibitor', lane: 'BOT_LANE' }), context).label).toBe('Took the bot inhibitor')
    expect(describeSwing(swing({ kind: 'dragon', monsterSubType: 'FIRE_DRAGON' }), context).label).toBe('Your Infernal Drake')
    expect(describeSwing(swing({ kind: 'baron', teamId: 200 }), context).label).toBe('Enemy Baron')
    expect(describeObjective({ ms: 0, monsterType: 'HORDE', monsterSubType: null, teamId: 200, delta: null }, 100))
      .toBe('Enemy Voidgrubs')
  })
})

describe('formatting', () => {
  it('signs points and keeps a decimal under one', () => {
    expect(formatSwingPoints(0.071)).toBe('+7')
    expect(formatSwingPoints(-0.04)).toBe('−4')
    expect(formatSwingPoints(0.006)).toBe('+0.6')
  })

  it('formats the game clock', () => {
    expect(formatGameClock(754_000)).toBe('12:34')
    expect(formatGameClock(5_000)).toBe('0:05')
  })

  it('turns a probability to either side', () => {
    expect(forTeam(0.7, 100, 'probability')).toBe(0.7)
    expect(forTeam(0.7, 200, 'probability')).toBeCloseTo(0.3)
  })
})

describe('resolveWinProbability', () => {
  it('prefers TrueMain\'s curve and has none without either source', () => {
    const stored = { points: [], swings: [], objectives: [] }
    expect(resolveWinProbability({ winProbability: stored, winProbabilityTimeline: null })).toBe(stored)
    expect(resolveWinProbability({})).toBeNull()
  })
})
