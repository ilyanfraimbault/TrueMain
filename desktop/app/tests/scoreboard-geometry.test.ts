import { describe, expect, it } from 'vitest'
import { DEFAULT_HUD } from '~/types/overlay'
import { platesBox, SCOREBOARD_ROWS, scoreboardRows } from '~/utils/scoreboard-geometry'

// The same values as `crates/shell-state/src/scoreboard.rs`'s tests: the shell
// places the window, this draws it on the Overlay page — they must agree.
describe('scoreboard geometry', () => {
  it('matches the measured 1080p scoreboard', () => {
    const rows = scoreboardRows({ width: 1920, height: 1080 }, { globalScale: 0.3 })
    expect(rows.centreX).toBe(960)
    expect(rows.pitch).toBeCloseTo(76.8, 0)
    expect(rows.top).toBeCloseTo(308.8, 0)
    expect(rows.width).toBeCloseTo(1152, 0)
  })

  it('grows upwards from a fixed bottom edge with the HUD scale', () => {
    const bottom = (scale: number) => {
      const rows = scoreboardRows({ width: 1920, height: 1080 }, { globalScale: scale })
      return { bottom: rows.top + rows.pitch * SCOREBOARD_ROWS, pitch: rows.pitch }
    }
    expect(bottom(0).pitch).toBeCloseTo(66.4, 1)
    expect(bottom(1).pitch).toBeCloseTo(66.4 * 1.52, 1)
    expect(bottom(0).bottom).toBeCloseTo(bottom(1).bottom, 6)
    // Out of range reads as its bound.
    expect(bottom(3).pitch).toBeCloseTo(bottom(1).pitch, 6)
  })

  it.each([
    [1920, 1080, 960, 360.8, 66.4],
    [2560, 1440, 1280, 481.1, 88.5],
    [3840, 2160, 1920, 721.6, 132.8],
    [3440, 1440, 1720, 481.1, 88.5],
    [1512, 982, 756, 349.9, 52.3],
  ])('places the rows on a %ix%i screen by its height (by its width below 16:9)', (width, height, centre, top, pitch) => {
    const rows = scoreboardRows({ width, height }, DEFAULT_HUD)
    expect(rows.centreX).toBeCloseTo(centre, 0)
    expect(Math.abs(rows.top - top)).toBeLessThan(0.5)
    expect(Math.abs(rows.pitch - pitch)).toBeLessThan(0.5)
  })

  it('moves by the nudge in 1080p pixels, capped', () => {
    const top = (nudge: number) => scoreboardRows({ width: 1280, height: 720 }, DEFAULT_HUD, nudge).top
    expect(top(-20) - top(0)).toBeCloseTo(-13.33, 1)
    expect(top(500)).toBeCloseTo(top(60), 6)
  })

  it('spans the five rows with the plates window, on the middle', () => {
    expect(platesBox({ width: 1920, height: 1080 }, { globalScale: 0.3 }, 0, 56)).toEqual({ x: 932, y: 309, width: 56, height: 384 })
  })
})
