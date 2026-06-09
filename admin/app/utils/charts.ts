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

// Pull the Nth default series color, wrapping around if there are more series
// than the palette defines.
export function defaultSeriesColor(index: number): string {
  return CHART_SERIES_PALETTE[index % CHART_SERIES_PALETTE.length]!
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
