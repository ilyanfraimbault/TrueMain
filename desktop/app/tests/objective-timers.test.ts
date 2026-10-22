import { describe, expect, it } from 'vitest'
import type { NeutralTimers } from '../app/types/game'
import { dragonLabel, formatCountdown, objectiveIcon, objectiveStatus, pillStatus } from '../app/utils/objective-timers'

describe('objectiveStatus', () => {
  it('counts down to the spawn, then stands up', () => {
    const drake = { at: 300, until: null }
    expect(objectiveStatus(drake, 120)).toEqual({ kind: 'countdown', seconds: 180 })
    expect(objectiveStatus(drake, 299.2)).toEqual({ kind: 'countdown', seconds: 1 })
    expect(objectiveStatus(drake, 300)).toEqual({ kind: 'up' })
  })

  it('is done once taken for good, or once a camp leaves the map', () => {
    expect(objectiveStatus({ at: null, until: null }, 1000)).toEqual({ kind: 'done' })
    const herald = { at: 900, until: 1185 }
    expect(objectiveStatus(herald, 1184)).toEqual({ kind: 'up' })
    expect(objectiveStatus(herald, 1185)).toEqual({ kind: 'done' })
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

describe('the dragon pit', () => {
  it('is a plain drake until the Rift names its element, then that element, then the Elder', () => {
    expect(dragonLabel({ elder: false, drake: null })).toBe('Drake')
    expect(objectiveIcon('dragon', { elder: false, drake: null })).toMatch(/\/dragon\.png$/)
    expect(dragonLabel({ elder: false, drake: 'hextech' })).toBe('Hextech')
    expect(objectiveIcon('dragon', { elder: false, drake: 'hextech' })).toMatch(/\/dragon_hextech\.png$/)
    expect(dragonLabel({ elder: true, drake: 'hextech' })).toBe('Elder')
    expect(objectiveIcon('dragon', { elder: true, drake: 'hextech' })).toMatch(/\/dragon_elder\.png$/)
  })

  it('takes the game\'s minimap icons for the other camps', () => {
    const timers = { elder: false, drake: null }
    expect(['grubs', 'herald', 'baron'].map(key => objectiveIcon(key as 'grubs', timers).split('/').pop())).toEqual(['grub.png', 'riftherald.png', 'baron.png'])
  })
})

describe('pillStatus', () => {
  const fresh: NeutralTimers = {
    dragon: { at: 300, until: null },
    elder: false,
    drake: null,
    grubs: { at: 480, until: 885 },
    herald: { at: 900, until: 1185 },
    baron: { at: 1200, until: null },
  }
  const GONE = { at: null, until: null }

  it('waits the Herald on the grubs and Baron on the Herald, never the pit or the grubs', () => {
    expect(pillStatus('dragon', fresh, 60)).toEqual({ kind: 'countdown', seconds: 240 })
    expect(pillStatus('grubs', fresh, 60)).toEqual({ kind: 'countdown', seconds: 420 })
    expect(pillStatus('herald', fresh, 60)).toEqual({ kind: 'waiting', seconds: 840 })
    expect(pillStatus('baron', fresh, 60)).toEqual({ kind: 'waiting', seconds: 1140 })
    // The grubs still standing hold the Herald back.
    expect(pillStatus('herald', fresh, 860)).toEqual({ kind: 'waiting', seconds: 40 })
  })

  it('counts down in earnest once the camp before is gone, taken or left', () => {
    expect(pillStatus('herald', { ...fresh, grubs: GONE }, 600)).toEqual({ kind: 'countdown', seconds: 300 })
    expect(pillStatus('herald', fresh, 890)).toEqual({ kind: 'countdown', seconds: 10 })
    expect(pillStatus('baron', { ...fresh, grubs: GONE, herald: GONE }, 1000)).toEqual({ kind: 'countdown', seconds: 200 })
    expect(pillStatus('baron', fresh, 1190)).toEqual({ kind: 'countdown', seconds: 10 })
  })

  it('leaves what stands or is gone as it is', () => {
    expect(pillStatus('baron', fresh, 1300)).toEqual({ kind: 'up' })
    expect(pillStatus('herald', fresh, 1185)).toEqual({ kind: 'done' })
  })
})
