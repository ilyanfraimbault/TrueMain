import { describe, expect, it } from 'vitest'
import {
  filterByPickRate,
  MAX_VARIATION_OPTIONS,
  MIN_VARIATION_PICKRATE,
  variationOptions,
} from '~~/shared/utils/build'

describe('MIN_VARIATION_PICKRATE', () => {
  it('is the 10% floor agreed for variation panels (#1466)', () => {
    expect(MIN_VARIATION_PICKRATE).toBe(0.1)
  })
})

describe('filterByPickRate', () => {
  it('drops options below the floor', () => {
    const options = [
      { id: 'core', pickRate: 0.62 },
      { id: 'alt', pickRate: 0.18 },
      { id: 'noise', pickRate: 0.005 }, // 0.5% — the long-tail row to hide
      { id: 'thin', pickRate: 0.055 }, // survived the old 5% floor, not this one
    ]
    expect(filterByPickRate(options).map(o => o.id)).toEqual(['core', 'alt'])
  })

  it('keeps options exactly at the floor (inclusive)', () => {
    const options = [{ id: 'edge', pickRate: 0.1 }]
    expect(filterByPickRate(options)).toEqual(options)
  })

  it('preserves order and does not mutate the input', () => {
    const options = [{ pickRate: 0.3 }, { pickRate: 0.01 }, { pickRate: 0.12 }]
    const result = filterByPickRate(options)
    expect(result).toEqual([{ pickRate: 0.3 }, { pickRate: 0.12 }])
    expect(options).toHaveLength(3)
  })

  it('returns an empty array when every option is below the floor', () => {
    expect(filterByPickRate([{ pickRate: 0.09 }, { pickRate: 0.001 }])).toEqual([])
  })
})

describe('variationOptions', () => {
  it('returns nothing when a single option clears the floor', () => {
    // The Nidalee 16.17 case: summoner spells at 100%, skill order at 99.3% —
    // a card that would restate the core block under a heading promising
    // alternatives.
    expect(variationOptions([{ pickRate: 1 }, { pickRate: 0.02 }])).toEqual([])
  })

  it('returns nothing when no option clears the floor', () => {
    expect(variationOptions([{ pickRate: 0.06 }, { pickRate: 0.04 }])).toEqual([])
  })

  it('keeps the alternatives when there is an actual choice', () => {
    const options = [
      { id: 'a', pickRate: 0.55 },
      { id: 'b', pickRate: 0.3 },
    ]
    expect(variationOptions(options).map(o => o.id)).toEqual(['a', 'b'])
  })

  it('caps at the three most played and sorts before capping', () => {
    const options = [
      { id: 'third', pickRate: 0.15 },
      { id: 'first', pickRate: 0.4 },
      { id: 'fourth', pickRate: 0.12 },
      { id: 'second', pickRate: 0.2 },
    ]
    expect(variationOptions(options).map(o => o.id)).toEqual(['first', 'second', 'third'])
    expect(MAX_VARIATION_OPTIONS).toBe(3)
  })

  it('does not mutate the input', () => {
    const options = [{ id: 'b', pickRate: 0.2 }, { id: 'a', pickRate: 0.5 }]
    variationOptions(options)
    expect(options.map(o => o.id)).toEqual(['b', 'a'])
  })
})
