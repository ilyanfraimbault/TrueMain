import { describe, expect, it } from 'vitest'
import vectors from '../../fixtures/overlay-geometry.json'
import type { OverlayAnchor } from '~/types/overlay'
import { customAt, panelBox } from '~/utils/overlay-layout'
import { platesBox, scoreboardRows } from '~/utils/scoreboard-geometry'

/** The shell's own answers (`crates/shell-state/tests/geometry_vectors.rs`); the editor must agree to the point. */
describe('the overlay editor places panels where the shell does', () => {
  it('puts a panel at its anchor or its dragged place', () => {
    for (const { anchor, custom, screen, size, origin } of vectors.placements) {
      const box = panelBox({ anchor: anchor as OverlayAnchor, custom }, size, screen)
      expect(Math.round(box.x), JSON.stringify({ anchor, custom, screen, size })).toBe(origin.x)
      expect(Math.round(box.y), JSON.stringify({ anchor, custom, screen, size })).toBe(origin.y)
    }
  })

  it('turns a drop into the same place', () => {
    for (const { screen, size, origin, custom } of vectors.drops) {
      const point = customAt(origin, size, screen)
      expect(point.x).toBeCloseTo(custom.x, 4)
      expect(point.y).toBeCloseTo(custom.y, 4)
    }
  })

  it('draws the scoreboard rows and the plates where the shell puts them', () => {
    for (const { frame, globalScale, nudge, rows, plates } of vectors.scoreboards) {
      const drawn = scoreboardRows(frame, { globalScale }, nudge)
      expect(drawn.centreX).toBeCloseTo(rows.centreX, 6)
      expect(drawn.top).toBeCloseTo(rows.top, 6)
      expect(drawn.pitch).toBeCloseTo(rows.pitch, 6)
      expect(drawn.width).toBeCloseTo(rows.width, 6)
      expect(platesBox(frame, { globalScale }, nudge, 60)).toEqual(plates)
    }
  })
})
