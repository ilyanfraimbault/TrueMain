import { CurveType, Orientation } from 'vue-chrts'

// Chart palette + axis helpers for the admin dashboard.
//
// TrueMain is intentionally emerald-only on surfaces, so every single-series
// chart picks up emerald-400. Callers needing a contrasting second series pass
// `categories[key].color` explicitly (amber-400 below).
export const CHART_PRIMARY = '#34d399' // emerald-400
export const CHART_ACCENT_AMBER = '#fbbf24' // amber-400
export const CHART_ACCENT_SKY = '#38bdf8' // sky-400

// Categorical series colours for a multi-series chart, in FIXED slot order:
// series 1 takes CHART_SERIES[0], series 2 CHART_SERIES[1], and so on. The order
// is the colourblind-safety mechanism, not decoration — emerald↔amber and
// amber↔sky are the adjacent pairs, and this ordering is the one that keeps both
// apart (worst adjacent CVD ΔE 10.6, normal-vision ΔE 21.2, OKLab ×100, measured
// against the dark card surface #18181b). Putting emerald next to sky instead
// collapses to ΔE 3.0 under tritanopia. Never cycle or reorder per chart; a
// fourth series folds into an "other" bucket or gets its own chart.
//
// Both are below 3:1 against the LIGHT surface, so any chart using them ships the
// per-series totals as visible text underneath — identity and magnitude are then
// readable without relying on the fill at all.
export const CHART_SERIES = [CHART_PRIMARY, CHART_ACCENT_AMBER, CHART_ACCENT_SKY] as const

// Axis tick text colour — zinc-400, matching `text-muted` so axis labels read
// as quiet metadata rather than competing with the data.
const CHART_AXIS_TEXT_COLOR = '#a1a1aa' // zinc-400
const CHART_AXIS_TEXT_SIZE = '11px'

// --- Nuxt UI dashboard styling --------------------------------------------
//
// The admin charts follow the Nuxt UI dashboard template aesthetic: minimal
// axes, no rotated labels, near-invisible gridlines, and a single emerald
// accent. Three chart shapes cover everything, and which one a series gets is
// decided by WHAT THE SERIES MEASURES, never by how it looks (#1218):
//   * flow, counted per period  -> VERTICAL bar chart   (`timeBarProps`)
//   * stock: a level at an instant, or a running total
//                               -> AREA/line chart      (`areaChartProps`)
//   * categorical top-N         -> HORIZONTAL bar chart (`horizontalBarProps`)
// The distinction is not cosmetic. A line drawn through per-period counts reads
// as a level that rose and fell, so a steady 350-a-day counter looks like a flat
// line doing nothing — which is exactly how the candidate funnel's `validated`
// series was misread. Bars say "this much moved, then this much"; a line says
// "it stood here, then here". Match the mark to the claim.
// The helpers below centralise that styling so every chart stays consistent;
// callers only pass data, categories and the per-chart formatters.
//
// (Keys verified against vue-chrts@2.1.4 `AxisConfig` / `AreaChartProps` /
// `BarChartProps`.)

// Muted, small tick text shared by both axes of every chart.
const AXIS_TEXT_CONFIG = {
  tickTextColor: CHART_AXIS_TEXT_COLOR,
  tickTextFontSize: CHART_AXIS_TEXT_SIZE,
} as const

// Trim long category labels (champion names like "Nunu & Willump", long
// snake_case table names) to a single flat line on the value/category axis
// instead of rotating them.
function trimmedAxisConfig(width: number) {
  return {
    ...AXIS_TEXT_CONFIG,
    tickTextFitMode: 'trim' as const,
    tickTextTrimType: 'end' as const,
    tickTextWidth: width,
  }
}

// Shared props for the "Matches over time" AREA chart. Emerald gradient fill
// (the category colour drives the gradient), a smooth monotone line, minimal
// axes with no gridlines, and a hover crosshair/tooltip. Spread onto
// `<NcAreaChart>` alongside `:data`, `:categories`, `:x-formatter`,
// `:y-formatter` and the per-chart x tick count. A function (not a const) so
// each call yields fresh, mutable `gradientStops`/config objects that satisfy
// the component's prop types.
export function areaChartProps() {
  return {
    curveType: CurveType.MonotoneX,
    lineWidth: 2,
    // Emerald fill that fades to transparent — the Nuxt UI "revenue" look.
    // Pin `stopColor` to emerald-400 explicitly: without it the gradient relies
    // on NcAreaChart injecting the series colour into each stop, and if it ever
    // doesn't, raw SVG defaults `stop-color` to black → a black→transparent fade.
    gradientStops: [
      { offset: '0%', stopColor: CHART_PRIMARY, stopOpacity: 0.4 },
      { offset: '100%', stopColor: CHART_PRIMARY, stopOpacity: 0 },
    ],
    // Quiet axes: keep the labels, drop every line/grid so the area is the focus.
    xGridLine: false,
    yGridLine: false,
    xDomainLine: false,
    yDomainLine: false,
    xTickLine: false,
    yTickLine: false,
    yNumTicks: 4,
    xAxisConfig: { ...AXIS_TEXT_CONFIG },
    yAxisConfig: { ...AXIS_TEXT_CONFIG },
    crosshairConfig: { color: CHART_PRIMARY, strokeColor: CHART_PRIMARY, strokeWidth: 1 },
    hideLegend: true,
    padding: { top: 8, right: 8, bottom: 4, left: 8 },
  }
}

// Shared props for a MULTI-SERIES area chart (candidate funnel #1024). Same
// styling as `areaChartProps()` with three differences that all follow from having
// more than one series:
//   * the legend is shown — with several series, identity may never be carried by
//     colour alone, so the legend is mandatory rather than optional chrome;
//   * the crosshair goes neutral, because an emerald one belongs to a chart whose
//     only series is emerald and would read as a fourth series here;
//   * `gradientStops` drops the pinned emerald `stopColor`. NcAreaChart writes each
//     stop's `stop-color` from the category's own colour and ignores the value we
//     pass (verified in vue-chrts@2.1.4 AreaChart.js), so pinning it would only
//     mislead the next reader into thinking every area is emerald.
// Callers still pass `:categories` (drawing colours from CHART_SERIES in order),
// and `:stacked` when the series sum to a meaningful whole.
export function multiAreaChartProps() {
  return {
    ...areaChartProps(),
    gradientStops: [
      { offset: '0%', stopOpacity: 0.4 },
      { offset: '100%', stopOpacity: 0 },
    ],
    hideLegend: false,
    crosshairConfig: {
      color: CHART_AXIS_TEXT_COLOR,
      strokeColor: CHART_AXIS_TEXT_COLOR,
      strokeWidth: 1,
    },
  }
}

// Shared props for a single-series VERTICAL time-series BAR chart (#1218). The
// same quiet styling as `areaChartProps()` — no gridlines, no domain/tick lines,
// muted tick text — with bars as the mark, because the series is a flow: a count
// of what happened during each bucket, not a level the system sat at.
//
// vue-chrts bar axes (vertical orientation): `x` is the DATA INDEX and `y` the
// value, so callers pass `:x-formatter` built from `indexLabelFormatter()` and
// `:y-formatter` for the value — the opposite mapping from `horizontalBarProps()`
// below, which flips them. `:y-axis` (the value keys) is required by the
// component and has no default, so every caller passes it.
export function timeBarProps() {
  return {
    // Small radius, not the categorical charts' 4: time-series bars are thin
    // (up to 90 daily buckets in a window) and a large radius eats the bar.
    radius: 2,
    barPadding: 0.2,
    xGridLine: false,
    yGridLine: false,
    xDomainLine: false,
    yDomainLine: false,
    xTickLine: false,
    yTickLine: false,
    yNumTicks: 4,
    xAxisConfig: { ...AXIS_TEXT_CONFIG },
    yAxisConfig: { ...AXIS_TEXT_CONFIG },
    hideLegend: true,
    padding: { top: 8, right: 8, bottom: 4, left: 8 },
  }
}

// Shared props for a MULTI-SERIES vertical time-series bar chart. Identical to
// `timeBarProps()` but with the legend shown: past one series, identity can never
// be carried by colour alone.
//
// Callers pass `:stacked` ONLY when the series sum to a meaningful whole (the
// candidate funnel's three intake sources do). Series that narrow out of one
// another, or nest inside one another — promoted ⊂ scored, retries ⊂ calls —
// stay grouped, because stacking them would draw a total that double-counts.
export function multiTimeBarProps() {
  return {
    ...timeBarProps(),
    hideLegend: false,
  }
}

// Turn a per-period flow into its running total, in place, over an already
// chronological series (#1218). Used for the series where the accumulated figure
// is the real quantity — "how many accounts have we validated" is a roster size,
// a stock, and therefore belongs on a line rather than in bars.
//
// `null` inputs are NOT zeros: they mark periods a counter did not yet exist for
// (see `validatedFirstMeasuredAtUtc`). Before the first measured period they stay
// `null`, so the curve STARTS where measurement started instead of running along
// the axis pretending the total was zero (#924); after it they hold the total
// flat, which is the only honest thing an unmeasured period can do to a total.
export function runningTotal(values: readonly (number | null)[]): (number | null)[] {
  let total: number | null = null
  return values.map((value) => {
    if (value === null) {
      return total
    }
    total = (total ?? 0) + value
    return total
  })
}

// Shared props for HORIZONTAL categorical bar charts (champions, candidate
// pipeline, top tables). The category axis runs down the LEFT (labels read
// flat — no rotation), the value axis along the bottom. Subtle styling: no
// gridlines, no domain/tick lines, muted tick text, rounded bar ends.
//
// IMPORTANT (Orientation.Horizontal axis mapping): in vue-chrts the bar `x`
// accessor is always the data index and `y` the value. With horizontal
// orientation unovis maps the VALUE to the bottom (x) axis and the data INDEX
// to the left (y) axis — so callers pass `:x-formatter` to format the value
// and `:y-formatter` to look the category label up by index, and the left axis
// trimming lives in `yAxisConfig`. (Verified against vue-chrts@2.1.4
// BarChart.js / @unovis/ts dataScale/valueScale.)
//
// `labelWidth` sizes both the left-axis label cap and the left padding so the
// longest label fits; pass the widest expected label width in px.
export function horizontalBarProps(labelWidth: number) {
  return {
    orientation: Orientation.Horizontal,
    radius: 4,
    barPadding: 0.2,
    xGridLine: false,
    yGridLine: false,
    xDomainLine: false,
    yDomainLine: false,
    xTickLine: false,
    yTickLine: false,
    xAxisConfig: { ...AXIS_TEXT_CONFIG },
    yAxisConfig: trimmedAxisConfig(labelWidth),
    hideLegend: true,
    padding: { top: 4, right: 16, bottom: 4, left: labelWidth + 12 },
  }
}

// Height for a bar chart that grows with its row count: `step` px per bar with
// a `min` floor so short lists don't collapse. Callers mirror the same height
// on their loading skeletons to avoid CLS.
export function barChartHeight(count: number, { min, step }: { min: number, step: number }): number {
  return Math.max(min, count * step)
}

// Build an `xFormatter` that maps the chart's numeric tick index back to a
// label. nuxt-charts feeds the tick's index for categorical x-axes, so we look
// the label up by position in the source array.
export function indexLabelFormatter<T>(
  data: T[],
  pick: (row: T) => string,
): (tick: number | Date) => string {
  return (tick: number | Date) => {
    const row = data[Number(tick)]
    return row ? pick(row) : ''
  }
}

// Compact integer formatter for y-axis ticks / counts (e.g. 12_400 -> "12,400").
export function formatCount(value: number | Date): string {
  return Number(value).toLocaleString('en-US')
}

// Tooltip title for the horizontal bar charts: the hovered datum's `label`.
// Shared by every categorical bar chart so the formatter isn't copy-pasted per
// page (champions, candidate pipeline, top champions).
export function labelTooltipTitle(d: { label: string }): string {
  return d.label
}

// Format a matches-over-time bucket key into an axis/tooltip label per
// granularity. Time buckets (`day`/`week`/`month`/`year`) arrive as ISO-8601 UTC
// timestamps of the period start and are formatted in UTC so the label matches
// the bucket boundary regardless of the viewer's timezone; `patch` buckets are
// already the human "MAJOR.MINOR" string and pass through untouched.
//   day   -> "2026-06-03"
//   week  -> "2026-06-01" (period start date)
//   month -> "Jun 2026"
//   year  -> "2026"
//   patch -> "16.4"
export function formatBucketLabel(
  bucket: string,
  granularity: 'day' | 'week' | 'month' | 'year' | 'patch',
): string {
  if (granularity === 'patch') {
    return bucket
  }
  const date = new Date(bucket)
  if (Number.isNaN(date.getTime())) {
    // Defensive: surface the raw key rather than "Invalid Date" if the backend
    // ever sends an unparseable bucket.
    return bucket
  }
  switch (granularity) {
    case 'day':
    case 'week':
      // ISO date (YYYY-MM-DD) in UTC; `sv-SE` yields that exact shape.
      return date.toLocaleDateString('sv-SE', { timeZone: 'UTC' })
    case 'month':
      return date.toLocaleDateString('en-US', {
        timeZone: 'UTC',
        month: 'short',
        year: 'numeric',
      })
    case 'year':
      return date.toLocaleDateString('en-US', {
        timeZone: 'UTC',
        year: 'numeric',
      })
  }
}
