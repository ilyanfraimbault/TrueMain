import { THEME_HEX } from '~~/shared/utils/theme-tokens'

// Default chart series colors.
//
// The first slot is the app primary (rosegold-400) so single-series charts pick
// up the brand accent automatically. Subsequent slots step down the `ink`
// neutral ramp rather than reaching for rainbow hues: a second series is
// usually a comparison baseline, and letting it take a saturated colour of its
// own would put it in competition with the data axis, where a hue *means*
// something. Callers needing genuinely categorical series should pass
// `categories[key].color` explicitly rather than padding this list.
export const CHART_SERIES_PALETTE = [
  THEME_HEX['rosegold-400'],
  THEME_HEX['ink-400'],
  THEME_HEX['ink-500'],
  THEME_HEX['ink-700'],
] as const

// Crosshair / grid / axis stroke. One step lighter than `--ui-border`
// (ink-800, `#26262c`) so guides stay legible against card backgrounds
// without slicing them.
export const CHART_GUIDE_COLOR = '#33333a' // between ink-800 and ink-700

// Pull the Nth default series color, wrapping around if the caller has
// more series than the palette defines.
export function defaultSeriesColor(index: number): string {
  return CHART_SERIES_PALETTE[index % CHART_SERIES_PALETTE.length]!
}
