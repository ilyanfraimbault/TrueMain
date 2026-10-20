import type { HudSettings } from '~/types/overlay'
import type { Box, Size } from '~/utils/overlay-layout'
import { SCOREBOARD_NUDGE } from '~/types/overlay'

/**
 * Where the game draws its TAB scoreboard's rows (#2186), for the Overlay
 * page's copy of the screen and the dev pages: the shell places the lane-gold
 * window by the same rule (`crates/shell-state/src/scoreboard.rs`, whose tests
 * hold the same values as `tests/scoreboard-geometry.test.ts`).
 *
 * Centred across the window, five rows growing upwards from a fixed bottom
 * edge as the HUD scale grows, sized by the window's height against 1080p
 * (by its width below 16:9). Measured on a real 1920 × 1080 TAB screenshot at
 * a HUD scale of about 0.3, the growth with the scale on the ability bar
 * between League's 0 and 100: **still to be confirmed in a game played at
 * several HUD scales** — the nudge covers what they miss.
 */

const REFERENCE = { width: 1920, height: 1080 } as const
export const SCOREBOARD_ROWS = 5
const ROW_PITCH_AT_ZERO = 66.4
const HUD_GROWTH = 0.52
const ROWS_BOTTOM_BELOW_MIDDLE = 152.8
const BOARD_WIDTH_AT_ZERO = 996.5

export interface ScoreboardRows {
  /** The board's middle, across. */
  centreX: number
  /** The first row's top edge. */
  top: number
  /** One row's height. */
  pitch: number
  /** The board's width. */
  width: number
}

const clamp = (value: number, min: number, max: number) => Math.min(Math.max(value, min), max)

/** The scoreboard's rows on a game window of `frame`, the player's nudge (1080p pixels, up when negative) included. */
export function scoreboardRows(frame: Size, hud: Pick<HudSettings, 'globalScale'>, nudge = 0): ScoreboardRows {
  const unit = Math.min(frame.height / REFERENCE.height, frame.width / REFERENCE.width)
  const growth = 1 + HUD_GROWTH * clamp(hud.globalScale, 0, 1)
  const pitch = ROW_PITCH_AT_ZERO * growth * unit
  const bottom = frame.height / 2 + (ROWS_BOTTOM_BELOW_MIDDLE + clamp(nudge, -SCOREBOARD_NUDGE, SCOREBOARD_NUDGE)) * unit
  return {
    centreX: frame.width / 2,
    top: bottom - pitch * SCOREBOARD_ROWS,
    pitch,
    width: BOARD_WIDTH_AT_ZERO * growth * unit,
  }
}

/** The lane-gold window on `frame`: `width` wide, on the board's middle, as tall as its rows — the shell's `plates_rect`. */
export function platesBox(frame: Size, hud: Pick<HudSettings, 'globalScale'>, nudge: number, width: number): Box {
  const rows = scoreboardRows(frame, hud, nudge)
  return {
    x: Math.round(rows.centreX - width / 2),
    y: Math.round(rows.top),
    width: Math.round(width),
    height: Math.round(rows.pitch * SCOREBOARD_ROWS),
  }
}
