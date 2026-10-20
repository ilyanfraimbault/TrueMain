import { describe, expect, it } from 'vitest'
import { monotonePath } from '~/utils/monotone-path'

/** Every number of a path, in order. */
const numbers = (path: string) => [...path.matchAll(/-?\d+(\.\d+)?/g)].map(match => Number(match[0]))

describe('monotonePath', () => {
  it('draws nothing without a point', () => {
    expect(monotonePath([])).toBe('')
  })

  it('draws two points straight', () => {
    expect(monotonePath([{ x: 0, y: 10 }, { x: 100, y: 20 }])).toBe('M0.0,10.0L100.0,20.0')
  })

  it('curves through every point', () => {
    const path = monotonePath([{ x: 0, y: 50 }, { x: 50, y: 10 }, { x: 100, y: 40 }, { x: 150, y: 30 }])
    expect(path.startsWith('M0.0,50.0C')).toBe(true)
    expect(path.match(/C/g)).toHaveLength(3)
    expect(path.endsWith(',150.0,30.0')).toBe(true)
  })

  it('never overshoots a point: every control point stays between its segment’s ends', () => {
    const points = [{ x: 0, y: 80 }, { x: 40, y: 10 }, { x: 80, y: 90 }, { x: 120, y: 85 }, { x: 160, y: 0 }]
    const values = numbers(monotonePath(points)).slice(2)
    for (let i = 0; i < points.length - 1; i++) {
      const [, c1y, , c2y] = values.slice(i * 6, i * 6 + 6)
      const low = Math.min(points[i]!.y, points[i + 1]!.y)
      const high = Math.max(points[i]!.y, points[i + 1]!.y)
      for (const y of [c1y!, c2y!]) {
        expect(y).toBeGreaterThanOrEqual(low - 0.05)
        expect(y).toBeLessThanOrEqual(high + 0.05)
      }
    }
  })

  it('stays flat on a level run', () => {
    const values = numbers(monotonePath([{ x: 0, y: 20 }, { x: 10, y: 20 }, { x: 20, y: 20 }]))
    expect(new Set(values.filter((_, i) => i % 2 === 1))).toEqual(new Set([20]))
  })
})
