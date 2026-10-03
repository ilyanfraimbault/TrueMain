import type { OverlayPanel, OverlayPanelSettings } from '~/types/overlay'

/**
 * Where the overlay panels sit on the screen, for the layout editor (#1819):
 * the same rule the shell places their windows by (`OverlaySettings::origin`
 * in `crates/shell-state/src/overlay.rs`), in screen points.
 */

export interface Size { width: number, height: number }
export interface Point { x: number, y: number }
export interface Box extends Point, Size {}

/** Mirrors `EDGE_MARGIN` and `TOP_MARGIN`. */
const EDGE_MARGIN = 16
const TOP_MARGIN = 56

/** Each panel's width, as `pages/overlay/[panel].vue` lays it out. */
const WIDTHS: Record<OverlayPanel, number> = {
  'next-item': 232,
  'win-probability': 160,
  'item-value': 300,
  'stats': 176,
  'loading': 440,
}

/**
 * Each panel's height in a game. A panel's window takes its content's size, so
 * this is the usual one, not a fixed one: close enough to lay the panels out.
 */
const HEIGHTS: Record<OverlayPanel, number> = {
  'next-item': 60,
  'win-probability': 48,
  'item-value': 200,
  'stats': 84,
  'loading': 260,
}

/** The screen a browser-only `npm run dev` lays the panels on. */
const FALLBACK_SCREEN: Size = { width: 1920, height: 1080 }

/** The screen this window is on, in points: the one the game is usually played on. */
export function currentScreen(): Size {
  if (typeof window === 'undefined' || !window.screen.width || !window.screen.height) return FALLBACK_SCREEN
  return { width: window.screen.width, height: window.screen.height }
}

/** A panel's size on screen, at the overlay's scale. */
export function panelSize(panel: OverlayPanel, scale: number): Size {
  return { width: WIDTHS[panel] * scale, height: HEIGHTS[panel] * scale }
}

/** `value` moved just enough for `[value, value + extent]` to fit `[0, length]`. */
function clampInto(value: number, length: number, extent: number) {
  return Math.min(Math.max(value, 0), Math.max(length - extent, 0))
}

/** A panel's box on `screen`: where it was dragged, or its anchor's spot. */
export function panelBox(settings: OverlayPanelSettings, size: Size, screen: Size): Box {
  let x: number
  let y: number
  if (settings.custom) {
    x = settings.custom.x * screen.width - size.width / 2
    y = settings.custom.y * screen.height - size.height / 2
  }
  else {
    const [row, column] = settings.anchor.split('-') as ['top' | 'center', 'left' | 'center' | 'right']
    x = column === 'left' ? EDGE_MARGIN : column === 'right' ? screen.width - size.width - EDGE_MARGIN : (screen.width - size.width) / 2
    y = row === 'top' ? TOP_MARGIN : (screen.height - size.height) / 2
  }
  return {
    x: clampInto(x, screen.width, size.width),
    y: clampInto(y, screen.height, size.height),
    ...size,
  }
}

/**
 * The custom position of a panel whose top-left corner is dropped at `origin`:
 * its centre, in fractions of the screen, with the whole panel kept on it.
 */
export function customAt(origin: Point, size: Size, screen: Size): Point {
  const x = clampInto(origin.x, screen.width, size.width) + size.width / 2
  const y = clampInto(origin.y, screen.height, size.height) + size.height / 2
  const fraction = (value: number, length: number) => Math.round(Math.min(Math.max(value / length, 0), 1) * 10_000) / 10_000
  return { x: fraction(x, screen.width), y: fraction(y, screen.height) }
}
