import { describe, expect, it } from 'vitest'
import {
  PRESENCE_HIGH,
  PRESENCE_NOTABLE,
  presenceTone,
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

describe('presenceTone', () => {
  it('only colours the high end — a rare pick is not a bad one', () => {
    expect(presenceTone(0.2)).toBe('text-data-good')
    expect(presenceTone(0.07)).toBe('text-data-good-dim')
    expect(presenceTone(0.01)).toBe('text-muted')
    expect(presenceTone(0)).toBe('text-muted')
  })

  it('puts each boundary in the stronger band', () => {
    expect(presenceTone(PRESENCE_HIGH)).toBe('text-data-good')
    expect(presenceTone(PRESENCE_NOTABLE)).toBe('text-data-good-dim')
    expect(presenceTone(PRESENCE_NOTABLE - 0.001)).toBe('text-muted')
  })

  it('stays muted for a ban rate the patch never observed (#920)', () => {
    expect(presenceTone(null)).toBe('text-muted')
  })
})
