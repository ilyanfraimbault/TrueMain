/**
 * The recap timeline's view (the window of the video it shows, zoomed in like a
 * video editor's) and the player's playback speeds. Pure, so the component only
 * holds the state.
 */
import type { Moment } from '~/types/recordings'
import { momentSide } from '~/utils/recording-moments'

/** The window of the video across the timeline's width, ms. */
export interface TimelineView { startMs: number, spanMs: number }

/** Where `ms` sits across the view, as a CSS length — pinned to the view's edges. */
export function viewPercent(view: TimelineView, ms: number): string {
  const end = view.startMs + view.spanMs
  return `${(Math.min(Math.max(ms, view.startMs), end) - view.startMs) / view.spanMs * 100}%`
}

export const isInView = (view: TimelineView, ms: number) => ms >= view.startMs && ms <= view.startMs + view.spanMs

/** The timeline's rows, px: the events' row, then the panel under it — the curve's lane, or a bare strip without one. */
export const TIMELINE_PANEL_TOP = 20
export const TIMELINE_CURVE_HEIGHT = 64
export const TIMELINE_BARE_HEIGHT = 20

/** How close two events' dots may sit, px, before they read as one: closer, they are one dot. */
export const EVENT_CLUSTER_PX = 10

/**
 * Events close enough on screen to be one dot, and the instant it is drawn at
 * (the middle of the first and the last). A cluster reads as the side that
 * owns it: `ally` when every game event in it went the player's side's way,
 * `enemy` when none did, `mixed` otherwise; `marked` when it holds only
 * moments the player marked — a mark beside a game event leaves the colour
 * to the event, the mark's own line telling it apart.
 */
export interface EventCluster {
  ms: number
  moments: Moment[]
  side: 'ally' | 'enemy' | 'mixed' | 'marked'
}

/** `moments` grouped into dots no closer than `gapMs`, in order: a chain of near events is one cluster. */
export function clusterMoments(moments: Moment[], gapMs: number): EventCluster[] {
  const sorted = [...moments].sort((a, b) => a.videoMs - b.videoMs)
  const groups: Moment[][] = []
  for (const moment of sorted) {
    const last = groups.at(-1)
    if (last && moment.videoMs - last.at(-1)!.videoMs <= gapMs) last.push(moment)
    else groups.push([moment])
  }
  return groups.map((group) => {
    const sides = new Set(group.map(moment => momentSide(moment)).filter(side => side !== 'marked'))
    let side: EventCluster['side'] = 'marked'
    if (sides.size > 1) side = 'mixed'
    else if (sides.size === 1) side = [...sides][0]!
    return {
      ms: (group[0]!.videoMs + group.at(-1)!.videoMs) / 2,
      moments: group,
      side,
    }
  })
}

/** The cluster nearest `ms`, when one lies within `toleranceMs` of it. */
export function clusterNear(clusters: EventCluster[], ms: number, toleranceMs: number): EventCluster | null {
  let best: EventCluster | null = null
  for (const cluster of clusters) {
    const distance = Math.abs(cluster.ms - ms)
    if (distance <= toleranceMs && (!best || distance < Math.abs(best.ms - ms))) best = cluster
  }
  return best
}

/** The narrowest window the timeline zooms to: a few seconds across, enough to land the playhead on a frame's neighbourhood. */
export const MIN_VIEW_SPAN_MS = 4_000

/** How much one zoom step (a key, a button) narrows or widens the window. */
export const ZOOM_STEP = 2

/** Where the playhead lands when the view pages to follow it: a tenth in, so what comes next is in view. */
const REVEAL_AT = 0.1

const clamp = (value: number, min: number, max: number) => Math.min(Math.max(value, min), max)

export function fullView(lengthMs: number): TimelineView {
  return { startMs: 0, spanMs: Math.max(lengthMs, 1) }
}

/** The view kept inside the video, never narrower than the floor nor wider than the video. */
export function clampView(view: TimelineView, lengthMs: number): TimelineView {
  const length = Math.max(lengthMs, 1)
  const spanMs = clamp(view.spanMs, Math.min(MIN_VIEW_SPAN_MS, length), length)
  return { startMs: clamp(view.startMs, 0, length - spanMs), spanMs }
}

export const isZoomed = (view: TimelineView, lengthMs: number) => view.spanMs < Math.max(lengthMs, 1) - 1

/**
 * Narrow (`factor` < 1) or widen (> 1) the window keeping `anchorMs` under the
 * same point of the timeline — under the pointer for a pinch, the playhead for a key.
 */
export function zoomView(view: TimelineView, lengthMs: number, anchorMs: number, factor: number): TimelineView {
  const target = clampView({ startMs: 0, spanMs: view.spanMs * factor }, lengthMs).spanMs
  const ratio = clamp((anchorMs - view.startMs) / view.spanMs, 0, 1)
  return clampView({ startMs: anchorMs - ratio * target, spanMs: target }, lengthMs)
}

export function panView(view: TimelineView, lengthMs: number, deltaMs: number): TimelineView {
  return clampView({ startMs: view.startMs + deltaMs, spanMs: view.spanMs }, lengthMs)
}

/** The view paged so `ms` is in it, like an editor's timeline following the playhead; unchanged when it already is. */
export function revealInView(view: TimelineView, lengthMs: number, ms: number): TimelineView {
  if (ms >= view.startMs && ms <= view.startMs + view.spanMs) return view
  return clampView({ startMs: ms - view.spanMs * REVEAL_AT, spanMs: view.spanMs }, lengthMs)
}

/** The clock labels' step: the smallest that keeps about eight across the window. */
export function tickStep(spanMs: number): number {
  const steps = [1_000, 2_000, 5_000, 10_000, 15_000, 30_000, 60_000, 120_000, 300_000, 600_000, 900_000]
  return steps.find(step => spanMs / step <= 8) ?? 1_800_000
}

/** The labelled instants inside the view, none at 0 nor so close to an edge that its label would spill over. */
export function viewTicks(view: TimelineView): number[] {
  const step = tickStep(view.spanMs)
  const end = view.startMs + view.spanMs
  const list: number[] = []
  for (let at = Math.ceil(view.startMs / step) * step; at <= end; at += step) {
    if (at > 0 && at - view.startMs >= step / 3 && end - at >= step / 3) list.push(at)
  }
  return list
}

export const PLAYBACK_RATES = [0.25, 0.5, 0.75, 1, 1.25, 1.5, 2, 4] as const

/** The next speed up (`1`) or down (`-1`) the list, staying at its ends. */
export function stepRate(rate: number, direction: 1 | -1): number {
  const index = PLAYBACK_RATES.findIndex(value => value >= rate)
  const current = index === -1 ? PLAYBACK_RATES.length - 1 : index
  const exact = PLAYBACK_RATES[current] === rate
  const next = direction === 1 ? (exact ? current + 1 : current) : current - 1
  return PLAYBACK_RATES[clamp(next, 0, PLAYBACK_RATES.length - 1)]!
}

export const formatRate = (rate: number) => `${rate}×`
