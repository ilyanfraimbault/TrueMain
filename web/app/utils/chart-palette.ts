// Default chart series colors for the emerald-based design system.
//
// The first slot is the app primary (emerald-400) so single-series charts
// pick it up automatically. Subsequent slots fall back to neutral zincs
// rather than rainbow hues — TrueMain is intentionally emerald-only on
// surfaces, and chart legends inherit that restraint. Callers needing more
// than two distinguishable series should pass `categories[key].color`
// explicitly with a chosen accent (e.g. amber-400 for "enemy", rose-400
// for "loss") rather than padding this list.
export const CHART_SERIES_PALETTE = [
  '#34d399', // emerald-400
  '#71717a', // zinc-500
  '#a1a1aa', // zinc-400
  '#3f3f46', // zinc-700
] as const

// Crosshair / grid / axis stroke. One shade lighter than `--ui-border`
// (zinc-800, `#27272a`) so guides stay legible against card backgrounds
// without slicing them.
export const CHART_GUIDE_COLOR = '#3f3f46' // zinc-700

// Pull the Nth default series color, wrapping around if the caller has
// more series than the palette defines.
export function defaultSeriesColor(index: number): string {
  return CHART_SERIES_PALETTE[index % CHART_SERIES_PALETTE.length]!
}
