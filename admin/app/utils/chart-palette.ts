// Chart palette for the admin portal.
//
// The portal's charts were the first part of the chrome moved onto the public
// site's palette (#1404): rosegold as the single accent, neutral guides, and a
// categorical ramp that only pays for a hue when a hue means something. The
// rest of the chrome (app.config.ts, main.css) caught up in #1409, so the
// portal and the site now share the same brand foundations end to end.
//
// What this file shares with `web/app/utils/chart-palette.ts` is the accent
// and the reasoning, not the file: the two answer different questions and are
// expected to keep diverging.
//   * the SERIES strategy is different, and deliberately. The public site's
//     `CHART_SERIES_PALETTE` is the accent followed by a descending grey ramp,
//     because a second series there is almost always a comparison baseline and a
//     saturated hue would compete with the data axis. The portal's `CHART_SERIES`
//     is a genuinely categorical triad, because its series are independent
//     sources — ladder, harvest and manual seed fail separately, and the reader
//     has to tell which one dried up.
//   * the NEUTRAL values below are still their own literals rather than a
//     reference to the `ink` ramp now shared with the chrome — chosen for
//     chart-guide contrast specifically, not surface material, so there is no
//     reason to couple them to a ramp stop that could move for unrelated
//     reasons.

// The app accent, and the first slot of every series list: a single-series
// chart picks it up without asking.
export const CHART_PRIMARY = '#e58f83' // rosegold-400

// The two remaining categorical hues.
export const CHART_ACCENT_SKY = '#38bdf8' // sky-400
export const CHART_ACCENT_AMBER = '#fbbf24' // amber-400

// The three extra hues the candidate-state chart spends (#1403). Not part of the
// triad above and not interchangeable with it — see CHART_SERIES.
const CHART_EXTRA_ORANGE = '#fb923c' // orange-400
const CHART_EXTRA_YELLOW = '#fde047' // yellow-300
const CHART_EXTRA_MINT = '#6ee7b7' // mint

// Categorical series colours for a multi-series chart, in FIXED slot order:
// series 1 takes CHART_SERIES[0], series 2 CHART_SERIES[1], and so on.
//
// **The first three are the palette.** Measured as OKLab ΔE ×100 under Machado
// severity-1.0 simulation (protanopia / deuteranopia / tritanopia):
//
//                                worst adjacent pair   worst pair overall
//   green > amber > sky (old)            10.6                 3.0
//   rosegold > sky > amber (this)        17.0                 9.4
//
// The old triad separated its adjacent pairs and collapsed on the pair it never
// placed side by side — green↔sky reads as ΔE 3.0 under tritanopia — which is
// why it carried a "never cycle or reorder" rule to stay safe. This one holds
// on EVERY pair, so a chart that skips a slot or reorders is still legible.
//
// **Slots 4-6 are a deliberate downgrade, added for one chart** (#1403: five
// candidate statuses plus the demotion curve on one axis, because the levels were
// asked for on the existing curve rather than in cards of their own). They were
// chosen by search over the same ΔE model, and the result has to be stated rather
// than assumed: at six series the set holds at **8.2 adjacent / 5.0 overall**
// (rosegold↔orange), so the property this triad was picked for — legible whatever
// the order — does NOT survive past slot three. Adjacency is load-bearing again up
// there, and colour alone no longer separates every pair.
//
// So: a fourth series is still the wrong instinct. Fold it into an "other" bucket
// or give it its own chart, and reach past slot three only when a single axis is
// genuinely the requirement. When it is, the mitigation is not optional — every
// series' current value ships as visible text under the chart, which is what
// carries identity once the fills stop doing it. (All six sit below 3:1 against a
// light surface, so that text was already carrying magnitude.)
export const CHART_SERIES = [
  CHART_PRIMARY,
  CHART_ACCENT_SKY,
  CHART_ACCENT_AMBER,
  CHART_EXTRA_ORANGE,
  CHART_EXTRA_YELLOW,
  CHART_EXTRA_MINT,
] as const

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
