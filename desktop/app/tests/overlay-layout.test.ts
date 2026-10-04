import { describe, expect, it } from 'vitest'
import { customAt, panelBox } from '~/utils/overlay-layout'

const screen = { width: 2560, height: 1440 }
const at = (custom: { x: number, y: number }) => ({ enabled: true, anchor: 'top-left' as const, custom })

describe('overlay layout', () => {
  it('keeps a panel dropped against an edge against it, whatever its size', () => {
    const custom = customAt({ x: 2560 - 232, y: 0 }, { width: 232, height: 60 }, screen)
    expect(custom).toEqual({ x: 1, y: 0 })
    const box = panelBox(at(custom), { width: 300, height: 200 }, screen)
    expect(box.x + box.width).toBe(screen.width)
    expect(box.y).toBe(0)
  })

  it('places a panel back where it was dropped', () => {
    const size = { width: 176, height: 84 }
    const box = panelBox(at(customAt({ x: 1000, y: 400 }, size, screen)), size, screen)
    expect(box.x).toBeCloseTo(1000, 0)
    expect(box.y).toBeCloseTo(400, 0)
  })

  it('never puts a panel off the screen', () => {
    const custom = customAt({ x: 9999, y: -50 }, { width: 160, height: 48 }, screen)
    expect(custom).toEqual({ x: 1, y: 0 })
  })
})
