// Chart palette for the admin portal.
//
// The portal's *chrome* is emerald on zinc and stays that way (`app.config.ts`)
// — restyling the whole portal is still scoped out of the redesign epic (#1059).
// Its *charts*, however, are on the public site's palette: rosegold as the
// single accent, neutral guides, and a categorical ramp that only pays for a
// hue when a hue means something (#1404). Charts were the visible half of the
// divergence, because a chart is nothing but colour carrying meaning.
//
// Deliberately a mirror of `web/app/utils/chart-palette.ts` so the two files
// diff cleanly. The one thing that does NOT cross over is the neutral: the web
// app's guides are drawn from its `ink` ramp, which would sit wrong on the
// portal's zinc surfaces, so the guide and axis neutrals below are zinc.

// The app accent, and the first slot of every series list: a single-series
// chart picks it up without asking.
export const CHART_PRIMARY = '#e58f83' // rosegold-400

// The two remaining categorical hues.
export const CHART_ACCENT_SKY = '#38bdf8' // sky-400
export const CHART_ACCENT_AMBER = '#fbbf24' // amber-400

// Categorical series colours for a multi-series chart, in FIXED slot order:
// series 1 takes CHART_SERIES[0], series 2 CHART_SERIES[1], and so on.
//
// The order is no longer load-bearing for accessibility, and that is the point
// of this triad rather than its predecessor. Measured as OKLab ΔE ×100 under
// Machado severity-1.0 simulation (protanopia / deuteranopia / tritanopia):
//
//                                worst adjacent pair   worst pair overall
//   emerald > amber > sky (old)          10.6                 3.0
//   rosegold > sky > amber (this)        17.0                 9.4
//
// The old triad separated its adjacent pairs and collapsed on the pair it never
// placed side by side — emerald↔sky reads as ΔE 3.0 under tritanopia — which is
// why it carried a "never cycle or reorder" rule to stay safe. This one holds
// on EVERY pair, so a chart that skips a slot or reorders is still legible.
// A fourth series still folds into an "other" bucket or gets its own chart.
//
// All three sit below 3:1 against a light surface, so any chart using them
// ships the per-series totals as visible text underneath — identity and
// magnitude stay readable without relying on the fill at all.
export const CHART_SERIES = [CHART_PRIMARY, CHART_ACCENT_SKY, CHART_ACCENT_AMBER] as const

// Crosshair / grid / axis stroke. One step lighter than `--ui-border`
// (zinc-800, `#27272a`) so guides stay legible against card backgrounds without
// slicing them. The web app's equivalent is `#33333a` on its ink ramp; this is
// the same idea one ramp over.
export const CHART_GUIDE_COLOR = '#3f3f46' // zinc-700

// Axis tick text — zinc-400, matching `text-muted` so axis labels read as quiet
// metadata rather than competing with the data.
export const CHART_AXIS_TEXT_COLOR = '#a1a1aa' // zinc-400

// Pull the Nth default series colour, wrapping around if the caller has more
// series than the palette defines.
export function defaultSeriesColor(index: number): string {
  return CHART_SERIES[index % CHART_SERIES.length]!
}
