/**
 * Variant B ("Spotlight") — the one cursor listener behind every light on the page.
 *
 * Three jobs, one rAF frame, and nothing at all while the cursor rests:
 *
 * 1. The torch: a soft glow (a DOM disc moved by `transform`, so the compositor
 *    alone moves it) trails the cursor with a little inertia.
 * 2. The wall texture: a small canvas, moved with the glow, draws the dot grid
 *    only where the torch falls. The grid is anchored to the document, so it
 *    scrolls with the page like a texture on a wall, and the dots bulge a touch
 *    under the light like under a lens.
 * 3. Lit edges: every `.surface` and every `[data-b-spot]` element near the
 *    cursor gets `--b-mx` / `--b-my` (px, element-relative), `--b-u` / `--b-v`
 *    (0..1 fractions) and `--b-spot` (0/1). Those are registered as
 *    non-inherited properties (design-b.css), so a write restyles the element
 *    alone, never its subtree — a 2,000-cell table lights its border for the
 *    price of one element.
 *
 * Reads (rects) happen in one pass, writes in the next, inside rAF only; the
 * scroll and pointer handlers just schedule a frame. Off-screen targets are
 * skipped (IntersectionObserver), and a MutationObserver flags the target list
 * as stale when the page changes.
 */

/** How far from an element's box the cursor still lights it (px). */
const NEAR = 380
/** Radius of the dot texture under the torch (CSS px). */
const DOT_RADIUS = 240
const GRID = 22
const MAX_DPR = 1.5
const SELECTOR = '.surface, .b-spot, [data-b-spot]'

export interface SpotlightEngineOptions {
  /** The glow disc, centred on its own origin (negative margins). */
  glow: HTMLElement
  /** The dot texture; null in lite / reduced motion. */
  canvas: HTMLCanvasElement | null
  /** Trail the cursor with inertia (off under reduced motion). */
  smooth: boolean
  onActiveChange: (active: boolean) => void
}

function distanceToRect(rect: DOMRect, x: number, y: number): number {
  const dx = Math.max(rect.left - x, 0, x - rect.right)
  const dy = Math.max(rect.top - y, 0, y - rect.bottom)
  return Math.hypot(dx, dy)
}

export function startSpotlightEngine(options: SpotlightEngineOptions): () => void {
  const { glow, canvas, smooth, onActiveChange } = options
  const ctx = canvas?.getContext('2d') ?? null
  const dpr = Math.min(window.devicePixelRatio || 1, MAX_DPR)
  const half = DOT_RADIUS + 6
  const size = half * 2
  if (canvas && ctx) {
    canvas.width = Math.round(size * dpr)
    canvas.height = Math.round(size * dpr)
    canvas.style.width = `${size}px`
    canvas.style.height = `${size}px`
  }

  let px = 0
  let py = 0
  let lx = 0
  let ly = 0
  let hasPointer = false
  let frame = 0
  let targetsDirty = true
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
    targetsDirty = true
    if (hasPointer) schedule()
  })
  mo.observe(document.body, { childList: true, subtree: true })

  function refreshTargets() {
    targetsDirty = false
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

  function drawDots() {
    if (!ctx || !canvas) return
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0)
    ctx.clearRect(0, 0, size, size)
    const ox = lx - half
    const oy = ly - half
    canvas.style.transform = `translate3d(${ox.toFixed(1)}px, ${oy.toFixed(1)}px, 0)`
    const sx = window.scrollX
    const sy = window.scrollY
    const r2 = DOT_RADIUS * DOT_RADIUS
    const i0 = Math.ceil((lx - DOT_RADIUS + sx) / GRID)
    const i1 = Math.floor((lx + DOT_RADIUS + sx) / GRID)
    const j0 = Math.ceil((ly - DOT_RADIUS + sy) / GRID)
    const j1 = Math.floor((ly + DOT_RADIUS + sy) / GRID)
    ctx.fillStyle = 'rgb(246 200 182)'
    for (let i = i0; i <= i1; i++) {
      const dx = i * GRID - sx - lx
      for (let j = j0; j <= j1; j++) {
        const dy = j * GRID - sy - ly
        const d2 = dx * dx + dy * dy
        if (d2 > r2) continue
        const f = 1 - Math.sqrt(d2) / DOT_RADIUS
        const k = f * f
        // A faint lens: the dots under the light swell outward a few pixels.
        const bulge = 1 + 0.085 * k
        const x = half + dx * bulge
        const y = half + dy * bulge
        const major = ((i % 4) + 4) % 4 === 0 && ((j % 4) + 4) % 4 === 0
        if (major) {
          ctx.globalAlpha = Math.min(1, k * 0.9)
          ctx.fillRect(x - 2.5, y - 0.5, 5, 1)
          ctx.fillRect(x - 0.5, y - 2.5, 1, 5)
        }
        else {
          ctx.globalAlpha = k * 0.5
          const s = 0.9 + f * 0.5
          ctx.fillRect(x - s / 2, y - s / 2, s, s)
        }
      }
    }
    ctx.globalAlpha = 1
  }

  function tick() {
    frame = 0
    if (targetsDirty) refreshTargets()

    let moving = false
    if (hasPointer) {
      if (smooth) {
        lx += (px - lx) * 0.2
        ly += (py - ly) * 0.2
        moving = Math.abs(px - lx) > 0.3 || Math.abs(py - ly) > 0.3
        if (!moving) {
          lx = px
          ly = py
        }
      }
      else {
        lx = px
        ly = py
      }
    }

    // Read every rect first, then write: no forced style/layout between them.
    const candidates = new Set<HTMLElement>([...visible, ...lit])
    const rects: [HTMLElement, DOMRect][] = []
    for (const el of candidates) rects.push([el, el.getBoundingClientRect()])

    glow.style.transform = `translate3d(${lx.toFixed(1)}px, ${ly.toFixed(1)}px, 0)`
    for (const [el, rect] of rects) {
      const near = hasPointer && rect.width > 0 && distanceToRect(rect, px, py) < NEAR
      const wasLit = lit.has(el)
      if (!near) {
        // Leave the light where it was and let `--b-spot` fade it out there.
        if (wasLit) {
          el.style.setProperty('--b-spot', '0')
          lit.delete(el)
        }
        continue
      }
      const s = el.style
      s.setProperty('--b-mx', `${(px - rect.left).toFixed(1)}px`)
      s.setProperty('--b-my', `${(py - rect.top).toFixed(1)}px`)
      s.setProperty('--b-u', ((px - rect.left) / rect.width).toFixed(4))
      s.setProperty('--b-v', rect.height > 0 ? ((py - rect.top) / rect.height).toFixed(4) : '0.5')
      if (!wasLit) {
        s.setProperty('--b-spot', '1')
        lit.add(el)
      }
    }

    if (hasPointer) drawDots()
    if (moving) schedule()
  }

  function setActive(active: boolean) {
    if (hasPointer === active) return
    hasPointer = active
    onActiveChange(active)
    schedule()
  }

  function onPointerMove(event: PointerEvent) {
    if (event.pointerType === 'touch') return
    px = event.clientX
    py = event.clientY
    if (!hasPointer) {
      // First sighting: the torch appears under the cursor, it does not fly in.
      lx = px
      ly = py
      setActive(true)
    }
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

  return () => {
    cancelAnimationFrame(frame)
    window.removeEventListener('pointermove', onPointerMove)
    window.removeEventListener('scroll', onScroll)
    window.removeEventListener('blur', onBlur)
    document.removeEventListener('mouseout', onMouseOut)
    io.disconnect()
    mo.disconnect()
    for (const el of lit) el.style.setProperty('--b-spot', '0')
    lit.clear()
  }
}

// Nuxt registers every file under `components/` as a component; a default export
// keeps that registration well-typed. Import the named function.
export default startSpotlightEngine
