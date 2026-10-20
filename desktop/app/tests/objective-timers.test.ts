import { describe, expect, it } from 'vitest'
import type { GameObjectives, TeamObjectives } from '../app/types/game'
import { drakeStandings, formatCountdown, objectiveStatus } from '../app/utils/objective-timers'

const team = (dragons: number): TeamObjectives => ({ turrets: 0, inhibitors: [], dragons, grubs: 0, baronUntil: null, elderUntil: null })
const objectives = (order: number, chaos: number): GameObjectives => ({ order: team(order), chaos: team(chaos), timers: null })

describe('objectiveStatus', () => {
  it('counts down to the spawn, then stands up', () => {
    const drake = { at: 300, until: null, takenBy: null }
    expect(objectiveStatus(drake, 120)).toEqual({ kind: 'countdown', seconds: 180 })
    expect(objectiveStatus(drake, 299.2)).toEqual({ kind: 'countdown', seconds: 1 })
    expect(objectiveStatus(drake, 300)).toEqual({ kind: 'up' })
  })

  it('is done once it will not come again, by whoever took it', () => {
    expect(objectiveStatus({ at: null, until: null, takenBy: 'CHAOS' }, 1000)).toEqual({ kind: 'done', by: 'CHAOS' })
  })

  it('is gone, by no one, once a camp leaves the map untaken', () => {
    const herald = { at: 900, until: 1185, takenBy: null }
    expect(objectiveStatus(herald, 1184)).toEqual({ kind: 'up' })
    expect(objectiveStatus(herald, 1185)).toEqual({ kind: 'done', by: null })
    // A grub taken before the camp left still leaves no one holding it.
    expect(objectiveStatus({ at: 480, until: 885, takenBy: 'ORDER' }, 900)).toEqual({ kind: 'done', by: null })
  })
})

describe('formatCountdown', () => {
  it('writes minutes and seconds, a started second counted whole', () => {
    expect(formatCountdown(75)).toBe('1:15')
    expect(formatCountdown(0.4)).toBe('0:01')
    expect(formatCountdown(0)).toBe('0:00')
    expect(formatCountdown(-3)).toBe('0:00')
    expect(formatCountdown(605)).toBe('10:05')
  })
})

describe('drakeStandings', () => {
  it('puts our side first and marks the soul point', () => {
    const { ours, theirs } = drakeStandings(objectives(1, 3), 'CHAOS')
    expect(ours).toEqual({ count: 3, soulPoint: true, soul: false })
    expect(theirs).toEqual({ count: 1, soulPoint: false, soul: false })
  })

  it('has no soul point left once a side holds the soul', () => {
    const { ours, theirs } = drakeStandings(objectives(4, 3), 'ORDER')
    expect(ours.soul).toBe(true)
    expect(theirs.soulPoint).toBe(false)
  })
})
