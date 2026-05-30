import { describe, expect, it } from 'vitest'
import { filterByPickRate, MIN_VARIATION_PICKRATE } from '~~/shared/utils/build'

describe('MIN_VARIATION_PICKRATE', () => {
  it('is the 5% floor agreed for variation panels', () => {
    expect(MIN_VARIATION_PICKRATE).toBe(0.05)
  })
})

describe('filterByPickRate', () => {
  it('drops options below the 5% floor', () => {
    const options = [
      { id: 'core', pickRate: 0.62 },
      { id: 'alt', pickRate: 0.18 },
      { id: 'noise', pickRate: 0.005 }, // 0.5% — the long-tail row to hide
    ]
    expect(filterByPickRate(options).map(o => o.id)).toEqual(['core', 'alt'])
  })

  it('keeps options exactly at the floor (inclusive)', () => {
    const options = [{ id: 'edge', pickRate: 0.05 }]
    expect(filterByPickRate(options)).toEqual(options)
  })

  it('preserves order and does not mutate the input', () => {
    const options = [{ pickRate: 0.3 }, { pickRate: 0.01 }, { pickRate: 0.1 }]
    const result = filterByPickRate(options)
    expect(result).toEqual([{ pickRate: 0.3 }, { pickRate: 0.1 }])
    expect(options).toHaveLength(3)
  })

  it('returns an empty array when every option is below the floor', () => {
    expect(filterByPickRate([{ pickRate: 0.04 }, { pickRate: 0.001 }])).toEqual([])
  })
})
