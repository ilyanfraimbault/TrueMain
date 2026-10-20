/** A point of a chart, in pixels. */
export interface PathPoint {
  x: number
  y: number
}

const format = (value: number) => value.toFixed(1)

/** The tangent at a middle point: zero at a turn, never steeper than its neighbours allow. */
function innerSlope(previous: PathPoint, point: PathPoint, next: PathPoint): number {
  const before = point.x - previous.x
  const after = next.x - point.x
  const slopeBefore = before ? (point.y - previous.y) / before : 0
  const slopeAfter = after ? (next.y - point.y) / after : 0
  const blended = before + after ? (slopeBefore * after + slopeAfter * before) / (before + after) : 0
  return (Math.sign(slopeBefore) + Math.sign(slopeAfter))
    * Math.min(Math.abs(slopeBefore), Math.abs(slopeAfter), 0.5 * Math.abs(blended)) || 0
}

/** The tangent at an end, from the segment it closes and the tangent at its other end. */
function endSlope(from: PathPoint, to: PathPoint, otherSlope: number): number {
  const width = to.x - from.x
  return width ? (3 * (to.y - from.y) / width - otherSlope) / 2 : otherSlope
}

/**
 * An SVG path through the points as a monotone cubic, the curve Unovis draws
 * by default (`d3.curveMonotoneX`), so the app's LP curve rolls like the
 * site's instead of zigzagging between snapshots. Monotone: between two points
 * the curve never rises above or dips below them, so it invents no LP. Fewer
 * than three points draw straight.
 */
export function monotonePath(points: readonly PathPoint[]): string {
  const [first] = points
  if (!first) return ''
  if (points.length < 3) return points.map((p, i) => `${i ? 'L' : 'M'}${format(p.x)},${format(p.y)}`).join('')

  const slopes = points.map((point, i) =>
    i === 0 || i === points.length - 1 ? 0 : innerSlope(points[i - 1]!, point, points[i + 1]!))
  slopes[0] = endSlope(first, points[1]!, slopes[1]!)
  const last = points.length - 1
  slopes[last] = endSlope(points[last - 1]!, points[last]!, slopes[last - 1]!)

  let path = `M${format(first.x)},${format(first.y)}`
  for (let i = 1; i < points.length; i++) {
    const from = points[i - 1]!
    const to = points[i]!
    const third = (to.x - from.x) / 3
    path += `C${format(from.x + third)},${format(from.y + third * slopes[i - 1]!)},`
      + `${format(to.x - third)},${format(to.y - third * slopes[i]!)},${format(to.x)},${format(to.y)}`
  }
  return path
}
