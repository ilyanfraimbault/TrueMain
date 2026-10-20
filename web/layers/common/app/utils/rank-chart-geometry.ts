import type { PathPoint } from '#common/utils/monotone-path'
import type { TierBoundary, TierZone } from '#common/utils/rank-chart-zones'

// The LP curve's pixels: the tier zones it is clipped by and the threshold
// lines drawn over it, for a plot `height` px tall whose snapshots sit at `xs`.

// How far past the plot an unbounded edge (Iron's floor, Challenger's ceiling,
// an apex tier nobody reached) is drawn, so no clip edge shows inside it.
const OVERSHOOT = 8

const format = (value: number) => value.toFixed(1)

export interface PlotFrame {
  xs: readonly number[]
  height: number
  domain: readonly [number, number]
}

export function scoreY(score: number, { domain: [yMin, yMax], height }: PlotFrame): number {
  if (score === Infinity) return -OVERSHOOT
  if (score === -Infinity) return height + OVERSHOOT
  if (yMax === yMin) return height / 2
  const y = height * (1 - (score - yMin) / (yMax - yMin))
  return Math.min(Math.max(y, -OVERSHOOT), height + OVERSHOOT)
}

// A boundary between two snapshots: straight when both ends were measured (the
// threshold moved from one day to the next), a step at the midpoint otherwise —
// where the floor is the snapshot's own tier read back, the colour changes
// halfway between the two snapshots, like the old per-snapshot colouring did.
function boundaryPoints(boundary: TierBoundary | null, fallback: number, frame: PlotFrame): PathPoint[] {
  const { xs } = frame
  const y = (i: number) => scoreY(boundary?.scores[i] ?? fallback, frame)
  const points: PathPoint[] = [{ x: xs[0]!, y: y(0) }]
  for (let i = 1; i < xs.length; i++) {
    const straight = boundary ? boundary.measured[i - 1] && boundary.measured[i] : true
    if (!straight) {
      const mid = (xs[i - 1]! + xs[i]!) / 2
      points.push({ x: mid, y: y(i - 1) }, { x: mid, y: y(i) })
    }
    points.push({ x: xs[i]!, y: y(i) })
  }
  return points
}

const polyline = (points: readonly PathPoint[]) =>
  points.map((p, i) => `${i ? 'L' : 'M'}${format(p.x)},${format(p.y)}`).join('')

/** The clip outline of one tier's band, its ceiling left to right then its floor back. */
export function zoneClipPath(zone: TierZone, frame: PlotFrame): string {
  const top = boundaryPoints(zone.upper, Infinity, frame)
  const bottom = boundaryPoints(zone.lower, -Infinity, frame).reverse()
  return `${polyline([...top, ...bottom])}Z`
}

/** True when any part of the band falls inside the plot. */
export function zoneVisible(zone: TierZone, frame: PlotFrame): boolean {
  return frame.xs.some((_, i) =>
    scoreY(zone.lower?.scores[i] ?? -Infinity, frame) > 0
    && scoreY(zone.upper?.scores[i] ?? Infinity, frame) < frame.height)
}

/**
 * The visible threshold line of a boundary: only where it was measured, and
 * only inside the plot. A lone measured snapshot draws a stub between its two
 * midpoints. Empty when nothing is drawn.
 */
export function boundaryLinePath(boundary: TierBoundary, frame: PlotFrame): string {
  const { xs } = frame
  const last = xs.length - 1
  const inPlot = (i: number) => {
    const y = scoreY(boundary.scores[i]!, frame)
    return boundary.measured[i] && y > 0 && y < frame.height
  }
  let path = ''
  for (let i = 0; i <= last; i++) {
    if (!inPlot(i)) continue
    const y = format(scoreY(boundary.scores[i]!, frame))
    const startsRun = i === 0 || !inPlot(i - 1)
    if (startsRun) path += `M${format(i === 0 ? xs[0]! : (xs[i - 1]! + xs[i]!) / 2)},${y}`
    path += `L${format(xs[i]!)},${y}`
    if (i < last && !inPlot(i + 1)) path += `L${format((xs[i]! + xs[i + 1]!) / 2)},${y}`
  }
  return path
}

/** A tier crest on the chart's y-axis. */
export interface CrestBand {
  tier: string
  top: number
}

// Each crest sits on the dashed line where its tier begins, at the first
// snapshot that line is measured and in view, so the icon reads as that line's
// label. A tier whose floor is out of view (the band the curve's bottom sits in)
// has no line to sit on and centres on its band's widest visible stretch instead —
// looked for where the band's edges were measured first: where an apex threshold
// is unknown, its band is read back from the snapshot's own tier and spans
// everything above Master, so Master and Grandmaster would centre on one spot.
export function crestBands(zones: readonly TierZone[], frame: PlotFrame): CrestBand[] {
  const onLines: CrestBand[] = []
  const centred: CrestBand[] = []
  for (const zone of zones) {
    const line = floorLineY(zone, frame)
    if (line !== null) {
      onLines.push({ tier: zone.tier, top: line })
      continue
    }
    const top = widestStretch(zone, frame, i => (zone.lower?.measured[i] ?? true) && (zone.upper?.measured[i] ?? true))
      ?? widestStretch(zone, frame, () => true)
    if (top !== null) centred.push({ tier: zone.tier, top })
  }
  // A centred crest makes way for the line crests: moved clear of the one it
  // overlaps when the plot has room, left out when not.
  const placed = centred.flatMap((crest) => {
    let top = crest.top
    for (const line of onLines) {
      if (Math.abs(top - line.top) < CREST_PX) top = top > line.top ? line.top + CREST_PX : line.top - CREST_PX
    }
    const fits = top >= CREST_PX / 2 && top <= frame.height - CREST_PX / 2
      && onLines.every(line => Math.abs(top - line.top) >= CREST_PX)
    return fits ? [{ tier: crest.tier, top }] : []
  })
  // Bottom to top, in climb order.
  return [...placed, ...onLines].sort((a, b) => b.top - a.top)
}

/** The crest's rendered size, the gap two crests need between their centres. */
const CREST_PX = 20
const MIN_SPAN = 12

function floorLineY(zone: TierZone, frame: PlotFrame): number | null {
  const lower = zone.lower
  if (!lower) return null
  for (let i = 0; i < frame.xs.length; i++) {
    const y = scoreY(lower.scores[i]!, frame)
    if (lower.measured[i] && y > 0 && y < frame.height) return y
  }
  return null
}

function widestStretch(zone: TierZone, frame: PlotFrame, eligible: (i: number) => boolean): number | null {
  let best: { span: number, top: number } | null = null
  frame.xs.forEach((_, i) => {
    if (!eligible(i)) return
    const floor = Math.min(scoreY(zone.lower?.scores[i] ?? -Infinity, frame), frame.height)
    const ceiling = Math.max(scoreY(zone.upper?.scores[i] ?? Infinity, frame), 0)
    const span = floor - ceiling
    if (span >= MIN_SPAN && (!best || span >= best.span)) best = { span, top: (floor + ceiling) / 2 }
  })
  return best === null ? null : (best as { top: number }).top
}
