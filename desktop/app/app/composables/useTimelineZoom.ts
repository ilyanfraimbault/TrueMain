import type { TimelineView } from '~/utils/recording-timeline'
import { clampView, fullView, isInView, isZoomed, panView, revealInView, viewPercent, ZOOM_STEP, zoomView } from '~/utils/recording-timeline'

/** A pan or zoom by hand holds the view where it was put this long before it pages to the playhead again. */
const HOLD_MS = 2_000

/**
 * The recap timeline's zoom, like a video editor's: a pinch or Ctrl/⌘/Alt + wheel
 * zooms around the pointer, − / = / \ zoom out, in around the playhead and fit the
 * video back. Zoomed in, a wheel pans, the view pages to follow the playhead, and
 * a drag past an edge scrolls it.
 */
export function useTimelineZoom(options: {
  lengthMs: () => number
  currentMs: () => number
  /** The track the view spans. */
  track: Ref<HTMLElement | null>
  /** A drag in progress holds the view: it scrolls by its own edge rule instead. */
  dragging: () => boolean
}) {
  const { lengthMs, currentMs, track, dragging } = options

  /** The window of the video across the track: the whole of it until zoomed. */
  const view = ref<TimelineView>(fullView(lengthMs()))
  const zoomed = computed(() => isZoomed(view.value, lengthMs()))
  watch(lengthMs, length => { view.value = fullView(length) })

  const pct = (ms: number) => viewPercent(view.value, ms)
  const inView = (ms: number) => isInView(view.value, ms)

  function msAt(clientX: number): number {
    const rect = track.value?.getBoundingClientRect()
    if (!rect || !rect.width) return 0
    return view.value.startMs + Math.min(Math.max((clientX - rect.left) / rect.width, 0), 1) * view.value.spanMs
  }

  let heldUntil = 0

  function setView(next: TimelineView, byHand = true) {
    view.value = clampView(next, lengthMs())
    if (byHand) heldUntil = performance.now() + HOLD_MS
  }

  /** A zoom step from the keys or the buttons, around the playhead when it is in view, else the window's middle. */
  function zoomStep(direction: 1 | -1) {
    const anchor = inView(currentMs()) ? currentMs() : view.value.startMs + view.value.spanMs / 2
    setView(zoomView(view.value, lengthMs(), anchor, direction === 1 ? 1 / ZOOM_STEP : ZOOM_STEP))
  }

  watch(currentMs, (ms) => {
    if (!zoomed.value || dragging() || performance.now() < heldUntil) return
    setView(revealInView(view.value, lengthMs(), ms), false)
  })

  function onWheel(event: WheelEvent) {
    const rect = track.value?.getBoundingClientRect()
    if (!rect?.width) return
    // A trackpad pinch arrives as a wheel with Ctrl held.
    if (event.ctrlKey || event.metaKey || event.altKey) {
      event.preventDefault()
      const delta = Math.min(Math.max(event.deltaY, -50), 50)
      setView(zoomView(view.value, lengthMs(), msAt(event.clientX), Math.exp(delta * 0.01)))
      return
    }
    if (!zoomed.value) return
    const delta = Math.abs(event.deltaX) > Math.abs(event.deltaY) ? event.deltaX : event.deltaY
    if (!delta) return
    event.preventDefault()
    setView(panView(view.value, lengthMs(), (delta / rect.width) * view.value.spanMs))
  }

  /** Dragging past an edge while zoomed scrolls the view that way, faster the further out. */
  function scrollPastEdge(clientX: number) {
    const rect = track.value?.getBoundingClientRect()
    if (!rect?.width || !zoomed.value) return
    const overshoot = clientX < rect.left ? clientX - rect.left : clientX > rect.right ? clientX - rect.right : 0
    if (overshoot) setView(panView(view.value, lengthMs(), (Math.max(Math.min(overshoot, 80), -80) / rect.width) * view.value.spanMs * 0.5))
  }

  defineShortcuts({
    '=': () => zoomStep(1),
    '+': () => zoomStep(1),
    '-': () => zoomStep(-1),
    '\\': () => setView(fullView(lengthMs())),
  })

  return { view, zoomed, pct, inView, msAt, setView, zoomStep, onWheel, scrollPastEdge }
}
