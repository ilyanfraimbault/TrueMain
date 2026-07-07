// Default chart series colors for the rose-gold design system.
//
// The first slot is the app primary (rosegold-400) so single-series charts
// pick it up automatically. Subsequent slots fall back to warm neutral mauves
// rather than rainbow hues — TrueMain is intentionally rose-gold-only on
// surfaces, and chart legends inherit that restraint. Callers needing more
// than two distinguishable series should pass `categories[key].color`
// explicitly with a chosen accent (e.g. sky-400 for "enemy", emerald-400
// for "win") rather than padding this list.
export const CHART_SERIES_PALETTE = [
  '#e58f83', // rosegold-400
  '#a99ba0', // mauve-400
  '#7f7276', // mauve-500
  '#524749', // mauve-700
] as const

// Crosshair / grid / axis stroke. One shade lighter than `--ui-border`
// (mauve-800, `#362f31`) so guides stay legible against card backgrounds
// without slicing them.
export const CHART_GUIDE_COLOR = '#4a4143' // between mauve-700 and mauve-800

// Pull the Nth default series color, wrapping around if the caller has
// more series than the palette defines.
export function defaultSeriesColor(index: number): string {
  return CHART_SERIES_PALETTE[index % CHART_SERIES_PALETTE.length]!
}
