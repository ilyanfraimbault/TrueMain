import type { Deployment } from '~~/shared/types/ops'

// Deploy markers on the time-series charts (#2297): where a version change lands among a
// chart's buckets. The chart wrappers draw markers by row INDEX — vue-chrts plots every
// series against the row index, not a time scale — so the mapping from a rollout time to a
// bucket happens here, once, and is tested apart from any chart.

/** One vertical rule on a chart: the row it sits on and what it says. */
export interface ChartMarker {
  /** Row index in the chart's `data`. */
  index: number
  /** The short label drawn beside the rule. */
  label: string
  /** Every deploy collapsed into this marker, one per line, for the hover title. */
  title: string
}

/**
 * Places `deployments` on a chart whose rows start at `bucketStarts` (ISO timestamps,
 * ascending). A deploy belongs to the last bucket that started at or before it; one before
 * the first bucket or after the end of the last is outside the chart and not drawn. The last
 * bucket is assumed as wide as the one before it, so a chart needs two rows to place anything.
 *
 * Several deploys in one bucket collapse into one marker: the Api and the ingestor lanes
 * rolling one release are one deploy to the reader, and two releases in a day bucket cannot
 * be told apart at that resolution anyway. The label names the latest version, with the count
 * of the others; the title lists them all.
 */
export function placeDeployMarkers(
  bucketStarts: readonly string[],
  deployments: readonly Deployment[],
): ChartMarker[] {
  const starts = bucketStarts.map(start => Date.parse(start))
  if (starts.length < 2 || starts.some(Number.isNaN)) {
    return []
  }
  const last = starts.length - 1
  const end = starts[last]! + (starts[last]! - starts[last - 1]!)

  const byIndex = new Map<number, Deployment[]>()
  for (const deploy of deployments) {
    const at = Date.parse(deploy.deployedAtUtc)
    if (Number.isNaN(at) || at < starts[0]! || at >= end) {
      continue
    }
    const index = lastIndexAtOrBefore(starts, at)
    byIndex.set(index, [...(byIndex.get(index) ?? []), deploy])
  }

  return [...byIndex.entries()]
    .sort(([a], [b]) => a - b)
    .map(([index, deploys]) => toMarker(index, deploys))
}

function lastIndexAtOrBefore(starts: readonly number[], at: number): number {
  let index = 0
  for (let i = 0; i < starts.length && starts[i]! <= at; i++) {
    index = i
  }
  return index
}

function toMarker(index: number, deploys: readonly Deployment[]): ChartMarker {
  const ordered = [...deploys].sort((a, b) => Date.parse(a.deployedAtUtc) - Date.parse(b.deployedAtUtc))
  const versions = [...new Set(ordered.map(deploy => deploy.version))]
  const latest = versions.at(-1)!
  const label = versions.length > 1 ? `${latest} (+${versions.length - 1})` : latest
  const title = ordered.map(describeDeploy).join('\n')
  return { index, label, title }
}

function describeDeploy(deploy: Deployment): string {
  const process = deploy.role ? `${deploy.processName} (${deploy.role})` : deploy.processName
  const from = deploy.previousVersion ? ` from ${deploy.previousVersion}` : ''
  const at = new Date(deploy.deployedAtUtc).toISOString().slice(0, 16).replace('T', ' ')
  return `${process} ${deploy.version}${from} · ${at} UTC`
}

/** One x-axis tick as rendered: its data value (a row index) and its pixel offset. */
export interface AxisTick {
  value: number
  position: number
}

/**
 * The row-index → pixel mapping of a rendered chart, read back from two of its x-axis ticks.
 * The upstream scale is linear in the index for both the area and the bar charts, so two
 * distinct ticks pin it exactly. Null when fewer than two distinct ticks are rendered — the
 * markers are then not drawn rather than drawn in the wrong place.
 */
export function indexToPixelFromTicks(ticks: readonly AxisTick[]): ((index: number) => number) | null {
  const usable = ticks.filter(tick => Number.isFinite(tick.value) && Number.isFinite(tick.position))
  if (usable.length < 2) {
    return null
  }
  const first = usable.reduce((a, b) => (b.value < a.value ? b : a), usable[0]!)
  const last = usable.reduce((a, b) => (b.value > a.value ? b : a), usable[0]!)
  if (last.value === first.value) {
    return null
  }
  const slope = (last.position - first.position) / (last.value - first.value)
  return index => first.position + (index - first.value) * slope
}

/** Two labels closer than this would overlap on one row. */
const LABEL_MIN_GAP_PX = 64

/**
 * The label row (0 or 1) of each marker, given their x positions in ascending order: a label
 * that would collide with the previous one takes the other row, so two deploys a bucket apart
 * stay readable.
 */
export function labelRows(positions: readonly number[]): number[] {
  const rows: number[] = []
  positions.forEach((x, i) => {
    const previous = positions[i - 1]
    const crowded = previous !== undefined && x - previous < LABEL_MIN_GAP_PX
    rows.push(crowded ? 1 - rows[i - 1]! : 0)
  })
  return rows
}
