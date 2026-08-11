import { describe, expect, it } from 'vitest'
import {
  BAN_RATE_HIGH,
  BAN_RATE_NOTABLE,
  banRateTone,
  PICK_RATE_HIGH,
  PICK_RATE_NOTABLE,
  pickRateTone,
  WIN_RATE_DECISIVE,
  WIN_RATE_EDGE,
  winRateTone,
} from '~/utils/rate-tone'

describe('winRateTone', () => {
  it('bands the gap from 50% into the five data-axis colours', () => {
    expect(winRateTone(0.56)).toBe('text-data-good')
    expect(winRateTone(0.52)).toBe('text-data-good-dim')
    expect(winRateTone(0.5)).toBe('text-data-mid')
    expect(winRateTone(0.48)).toBe('text-data-bad-dim')
    expect(winRateTone(0.44)).toBe('text-data-bad')
  })

  it('puts each boundary in the stronger band, symmetrically', () => {
    // The edges are the whole contract of a banding function: 51% must not read
    // as noise on one side while 49% reads as a deficit on the other.
    expect(winRateTone(0.5 + WIN_RATE_EDGE)).toBe('text-data-good-dim')
    expect(winRateTone(0.5 - WIN_RATE_EDGE)).toBe('text-data-bad-dim')
    expect(winRateTone(0.5 + WIN_RATE_DECISIVE)).toBe('text-data-good')
    expect(winRateTone(0.5 - WIN_RATE_DECISIVE)).toBe('text-data-bad')
  })

  it('stays muted when there is no win rate to colour', () => {
    expect(winRateTone(null)).toBe('text-muted')
    expect(winRateTone(undefined)).toBe('text-muted')
  })
})

describe('pickRateTone', () => {
  it('only colours the high end — a rare pick is not a bad one', () => {
    expect(pickRateTone(0.06)).toBe('text-data-good')
    expect(pickRateTone(0.03)).toBe('text-data-good-dim')
    expect(pickRateTone(0.003)).toBe('text-muted')
    expect(pickRateTone(0)).toBe('text-muted')
  })

  it('puts each boundary in the stronger band', () => {
    expect(pickRateTone(PICK_RATE_HIGH)).toBe('text-data-good')
    expect(pickRateTone(PICK_RATE_NOTABLE)).toBe('text-data-good-dim')
    expect(pickRateTone(PICK_RATE_NOTABLE - 0.001)).toBe('text-muted')
  })
})

describe('banRateTone', () => {
  it('runs an order of magnitude above pick rate, so it bands higher', () => {
    expect(banRateTone(0.3)).toBe('text-data-good')
    expect(banRateTone(0.09)).toBe('text-data-good-dim')
    expect(banRateTone(0.02)).toBe('text-muted')
    // The scales are genuinely different: 6% is a busy ban and an unheard-of pick.
    expect(banRateTone(0.06)).toBe('text-data-good-dim')
    expect(pickRateTone(0.06)).toBe('text-data-good')
  })

  it('puts each boundary in the stronger band', () => {
    expect(banRateTone(BAN_RATE_HIGH)).toBe('text-data-good')
    expect(banRateTone(BAN_RATE_NOTABLE)).toBe('text-data-good-dim')
    expect(banRateTone(BAN_RATE_NOTABLE - 0.001)).toBe('text-muted')
  })

  it('stays muted for a patch that never observed bans (#920)', () => {
    expect(banRateTone(null)).toBe('text-muted')
  })
})
