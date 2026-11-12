/**
 * Variant E ("Launcher") — the one pointer listener behind every light.
 *
 * Two jobs, one rAF frame, and nothing at all while the cursor rests:
 *
 * 1. Lit edges: every `.surface` and every `[data-e-spot]` element the cursor
 *    comes near gets `--e-mx` / `--e-my` (px, element-relative), `--e-u` /
 *    `--e-v` (0..1 fractions of its box) and `--e-spot` (0/1, eased in CSS).
 *    They are registered non-inherited in design-e.css, so a write restyles the
 *    element alone, never its subtree.
 * 2. Trackers: a component that wants the cursor relative to one element (the
 *    home stage's parallax) registers it with `trackPointer`; its rect is read in
 *    the same pass as the others and its callback runs in the write pass.
 *
 * Rects are all read first, then everything is written: no forced layout in
 * between. Off-screen targets are skipped (IntersectionObserver), and a
 * MutationObserver marks the target list stale when the page changes. Only for
 * a fine pointer: on touch there is no cursor to follow.
 *
 * No glow follows the cursor across the page and no texture is drawn: the light
 * only shows where it meets an edge.
 */

/** How far from an element's box the cursor still lights its edge (px). */
const NEAR = 220
const SELECTOR = '.surface, [data-e-spot]'

export interface PointerSample {
  /** 0..1 across the tracked element (may fall outside while the cursor is elsewhere). */
  u: number
  v: number
  inside: boolean
}

interface Tracker {
  el: HTMLElement
  onSample: (sample: PointerSample) => void
}

const trackers = new Set<Tracker>()
let wake: (() => void) | null = null
let stop: (() => void) | null = null
let users = 0

function distanceToRect(rect: DOMRect, x: number, y: number): number {
  const dx = Math.max(rect.left - x, 0, x - rect.right)
  const dy = Math.max(rect.top - y, 0, y - rect.bottom)
  return Math.hypot(dx, dy)
}

function start(): () => void {
  let px = 0
  let py = 0
  let hasPointer = false
  let frame = 0
  let dirty = true
  let known = new Set<HTMLElement>()
  const visible = new Set<HTMLElement>()
  const lit = new Set<HTMLElement>()

  const io = new IntersectionObserver((entries) => {
    for (const entry of entries) {
      const el = entry.target as HTMLElement
      if (entry.isIntersecting) visible.add(el)
      else visible.delete(el)
    }
    if (hasPointer) schedule()
  }, { rootMargin: `${NEAR}px` })

  const mo = new MutationObserver(() => {
    dirty = true
  })
  mo.observe(document.body, { childList: true, subtree: true })

  function refresh() {
    dirty = false
    const next = new Set(document.querySelectorAll<HTMLElement>(SELECTOR))
    for (const el of known) {
      if (next.has(el)) continue
      io.unobserve(el)
      visible.delete(el)
      lit.delete(el)
    }
    for (const el of next) {
      if (!known.has(el)) io.observe(el)
    }
    known = next
  }

  function schedule() {
    if (!frame) frame = requestAnimationFrame(tick)
  }

  function tick() {
    frame = 0
    if (dirty) refresh()

    // Read pass.
    const rects: [HTMLElement, DOMRect][] = []
    for (const el of new Set([...visible, ...lit])) rects.push([el, el.getBoundingClientRect()])
    const tracked: [Tracker, DOMRect][] = []
    for (const tracker of trackers) tracked.push([tracker, tracker.el.getBoundingClientRect()])

    // Write pass.
    for (const [el, rect] of rects) {
      const near = hasPointer && rect.width > 0 && distanceToRect(rect, px, py) < NEAR
      if (!near) {
        // Leave the light where it was; `--e-spot` fades it out there.
        if (lit.delete(el)) el.style.setProperty('--e-spot', '0')
        continue
      }
      const s = el.style
      s.setProperty('--e-mx', `${(px - rect.left).toFixed(1)}px`)
      s.setProperty('--e-my', `${(py - rect.top).toFixed(1)}px`)
      s.setProperty('--e-u', ((px - rect.left) / rect.width).toFixed(4))
      s.setProperty('--e-v', rect.height > 0 ? ((py - rect.top) / rect.height).toFixed(4) : '0.5')
      if (!lit.has(el)) {
        s.setProperty('--e-spot', '1')
        lit.add(el)
      }
    }
    for (const [tracker, rect] of tracked) {
      const inside = hasPointer && rect.width > 0
        && px >= rect.left && px <= rect.right && py >= rect.top && py <= rect.bottom
      tracker.onSample({
        u: rect.width > 0 ? (px - rect.left) / rect.width : 0.5,
        v: rect.height > 0 ? (py - rect.top) / rect.height : 0.5,
        inside,
      })
    }
  }

  function setActive(active: boolean) {
    if (hasPointer === active) return
    hasPointer = active
    schedule()
  }

  function onPointerMove(event: PointerEvent) {
    if (event.pointerType === 'touch') return
    px = event.clientX
    py = event.clientY
    hasPointer = true
    schedule()
  }
  function onMouseOut(event: MouseEvent) {
    if (!event.relatedTarget) setActive(false)
  }
  const onBlur = () => setActive(false)
  const onScroll = () => {
    if (hasPointer) schedule()
  }

  window.addEventListener('pointermove', onPointerMove, { passive: true })
  window.addEventListener('scroll', onScroll, { passive: true })
  window.addEventListener('blur', onBlur)
  document.addEventListener('mouseout', onMouseOut)
  wake = schedule

  return () => {
    cancelAnimationFrame(frame)
    window.removeEventListener('pointermove', onPointerMove)
    window.removeEventListener('scroll', onScroll)
    window.removeEventListener('blur', onBlur)
    document.removeEventListener('mouseout', onMouseOut)
    io.disconnect()
    mo.disconnect()
    for (const el of lit) el.style.setProperty('--e-spot', '0')
    lit.clear()
    wake = null
  }
}

/** Start the engine (ref-counted): the shell holds it for the whole session. */
export function acquireLightEngine(): () => void {
  if (!window.matchMedia('(hover: hover) and (pointer: fine)').matches) return () => {}
  users++
  if (users === 1) stop = start()
  let released = false
  return () => {
    if (released) return
    released = true
    users--
    if (users === 0) {
      stop?.()
      stop = null
    }
  }
}

/**
 * Follow the cursor relative to `el`; `onSample` runs inside the engine's write
 * pass, so it must only write. Nothing is sampled on touch-only devices.
 */
export function trackPointer(el: HTMLElement, onSample: (sample: PointerSample) => void): () => void {
  const tracker: Tracker = { el, onSample }
  trackers.add(tracker)
  wake?.()
  return () => {
    trackers.delete(tracker)
  }
}

// Nuxt registers every file under `components/` as a component; a default
// export keeps that registration well-typed. Import the named functions.
export default acquireLightEngine
