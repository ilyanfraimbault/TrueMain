import { CurveType, Orientation } from 'vue-chrts'

// Chart palette + axis helpers for the admin dashboard.
//
// Mirrors `web/app/utils/chart-palette.ts`: TrueMain is intentionally
// emerald-only on surfaces, so single-series charts pick up emerald-400 and
// extra series fall back to neutral zincs rather than rainbow hues. Callers
// needing a contrasting second series should pass `categories[key].color`
// explicitly (e.g. amber-400) rather than padding this list.
export const CHART_SERIES_PALETTE = [
  '#34d399', // emerald-400
  '#71717a', // zinc-500
  '#a1a1aa', // zinc-400
  '#3f3f46', // zinc-700
] as const

// Crosshair / grid / axis stroke. One shade lighter than `--ui-border`
// (zinc-800) so guides stay legible against card backgrounds.
export const CHART_GUIDE_COLOR = '#3f3f46' // zinc-700

// Axis tick text colour — zinc-400, matching `text-muted` so axis labels read
// as quiet metadata rather than competing with the data.
const CHART_AXIS_TEXT_COLOR = '#a1a1aa' // zinc-400
const CHART_AXIS_TEXT_SIZE = '11px'

// Pull the Nth default series color, wrapping around if there are more series
// than the palette defines.
export function defaultSeriesColor(index: number): string {
  return CHART_SERIES_PALETTE[index % CHART_SERIES_PALETTE.length]!
}

// --- Nuxt UI dashboard styling --------------------------------------------
//
// The admin charts follow the Nuxt UI dashboard template aesthetic: minimal
// axes, no rotated labels, near-invisible gridlines, and a single emerald
// accent. Two chart shapes cover everything:
//   * time-series  -> AREA chart (only "Matches over time")
//   * categorical  -> HORIZONTAL bar chart (labels read flat on the y-axis)
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
      { offset: '0%', stopColor: '#34d399', stopOpacity: 0.4 },
      { offset: '100%', stopColor: '#34d399', stopOpacity: 0 },
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
    crosshairConfig: { color: '#34d399', strokeColor: '#34d399', strokeWidth: 1 },
    hideLegend: true,
    padding: { top: 8, right: 8, bottom: 4, left: 8 },
  }
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
// granularity. Time buckets (`week`/`month`/`year`) arrive as ISO-8601 UTC
// timestamps of the period start and are formatted in UTC so the label matches
// the bucket boundary regardless of the viewer's timezone; `patch` buckets are
// already the human "MAJOR.MINOR" string and pass through untouched.
//   week  -> "2026-06-01" (period start date)
//   month -> "Jun 2026"
//   year  -> "2026"
//   patch -> "16.4"
export function formatBucketLabel(
  bucket: string,
  granularity: 'week' | 'month' | 'year' | 'patch',
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
