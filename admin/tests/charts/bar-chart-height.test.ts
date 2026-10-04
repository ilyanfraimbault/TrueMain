import { describe, expect, it } from 'vitest'
import { barChartHeight } from '~/utils/charts'

// Horizontal bar charts grow with their row count, floored so a short list does
// not collapse; the loading skeletons mirror the same height to avoid CLS (#690).

describe('barChartHeight', () => {
  const sizing = { min: 160, step: 28 }

  it('grows by one step per bar above the floor', () => {
    expect(barChartHeight(10, sizing)).toBe(280)
    expect(barChartHeight(11, sizing)).toBe(308)
  })

  it('applies the floor to short and empty lists', () => {
    expect(barChartHeight(0, sizing)).toBe(160)
    expect(barChartHeight(3, sizing)).toBe(160)
  })

  it('returns the floor exactly at the crossover', () => {
    expect(barChartHeight(5, { min: 140, step: 28 })).toBe(140)
    expect(barChartHeight(6, { min: 140, step: 28 })).toBe(168)
  })
})
