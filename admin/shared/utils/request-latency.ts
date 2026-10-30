import type { RouteLatency, RouteLatencyPoint } from '~~/shared/types/ops'

// Pure helpers behind the Processes → Performance tab (#2307): how a duration reads, which
// routes are slow, and the sparkline geometry. Kept out of the components so they are pinned
// by tests without mounting anything.

/** `42 ms` under a second, `1.25 s` / `12.3 s` above. */
export function formatLatency(ms: number | null | undefined): string {
  if (ms === null || ms === undefined || !Number.isFinite(ms)) {
    return '—'
  }
  if (ms < 1000) {
    return `${Math.round(ms)} ms`
  }
  const seconds = ms / 1000
  return `${seconds.toFixed(seconds < 10 ? 2 : 1)} s`
}

/** A route is slow when its p95 is above the payload's threshold. */
export function isSlowRoute(route: Pick<RouteLatency, 'p95Ms'>, thresholdMs: number): boolean {
  return route.p95Ms > thresholdMs
}

/** How many routes stand above the threshold. */
export function countSlowRoutes(routes: readonly Pick<RouteLatency, 'p95Ms'>[], thresholdMs: number): number {
  return routes.filter(route => isSlowRoute(route, thresholdMs)).length
}

export interface SparklinePoint {
  x: number
  y: number
}

export interface Sparkline {
  /** Runs of consecutive measured bins; an idle bin breaks the line instead of dropping it to zero. */
  segments: SparklinePoint[][]
  /** The threshold's height: the scale always reaches it. */
  thresholdY: number
}

/**
 * Lays a route's p95 series out in a `width` × `height` box. The vertical scale tops out at
 * the series' own slowest bin or the threshold, whichever is higher, so a route that never
 * nears a second stays low and the threshold line is drawn wherever it fits.
 */
export function sparkline(
  series: readonly RouteLatencyPoint[],
  width: number,
  height: number,
  thresholdMs: number,
): Sparkline {
  const measured = series.map(point => point.p95Ms).filter((value): value is number => value !== null)
  const top = Math.max(thresholdMs, ...measured, 1)
  const step = series.length > 1 ? width / (series.length - 1) : 0
  const yOf = (value: number) => height - (Math.min(value, top) / top) * height

  const segments: SparklinePoint[][] = []
  let current: SparklinePoint[] = []
  series.forEach((point, index) => {
    if (point.p95Ms === null) {
      if (current.length > 0) {
        segments.push(current)
        current = []
      }
      return
    }
    current.push({ x: series.length > 1 ? index * step : width / 2, y: yOf(point.p95Ms) })
  })
  if (current.length > 0) {
    segments.push(current)
  }

  return { segments, thresholdY: yOf(thresholdMs) }
}
