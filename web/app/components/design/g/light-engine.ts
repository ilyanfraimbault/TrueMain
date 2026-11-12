/**
 * Variant G ("Bento") — the one pointer listener behind every lit edge.
 *
 * Every `.surface` and every `[data-g-spot]` element near the cursor gets
 * `--g-mx` / `--g-my` (px, element-relative), `--g-u` / `--g-v` (0..1 fractions
 * of its box) and `--g-spot` (0/1). They are registered as non-inherited
 * properties (design-g.css), so a write restyles that element alone, never its
 * subtree. The CSS turns them into a lit border, the tilt of a champion card,
 * the parallax of a splash and the lean of the desktop tile's lamp.
 *
 * Idle costs nothing: no frame is scheduled unless the pointer moves or the page
 * scrolls under it. Reads (rects) happen in one pass, writes in the next, inside
 * one rAF. Off-screen targets are skipped (IntersectionObserver), and a
 * MutationObserver flags the target list as stale when the page changes.
 */

/** How far from an element's box the cursor still lights it (px). */
const NEAR = 340
const SELECTOR = '.surface, [data-g-spot]'

function distanceToRect(rect: DOMRect, x: number, y: number): number {
  const dx = Math.max(rect.left - x, 0, x - rect.right)
  const dy = Math.max(rect.top - y, 0, y - rect.bottom)
  return Math.hypot(dx, dy)
}

export function startLightEngine(): () => void {
  let px = 0
  let py = 0
  let hasPointer = false
  let frame = 0
  let targetsDirty = true
  let known = new Set<HTMLElement>()
  const visible = new Set<HTMLElement>()
  const lit = new Set<HTMLElement>()

  function schedule() {
    if (!frame) frame = requestAnimationFrame(tick)
  }

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

  function tick() {
    frame = 0
    if (targetsDirty) refreshTargets()

    const candidates = new Set<HTMLElement>([...visible, ...lit])
    const rects: [HTMLElement, DOMRect][] = []
    for (const el of candidates) {
      if (el.isConnected) rects.push([el, el.getBoundingClientRect()])
      else lit.delete(el)
    }

    for (const [el, rect] of rects) {
      const near = hasPointer && rect.width > 0 && distanceToRect(rect, px, py) < NEAR
      if (!near) {
        // Leave the light where it was and let `--g-spot` fade it out there.
        if (lit.has(el)) {
          el.style.setProperty('--g-spot', '0')
          lit.delete(el)
        }
        continue
      }
      const s = el.style
      s.setProperty('--g-mx', `${(px - rect.left).toFixed(1)}px`)
      s.setProperty('--g-my', `${(py - rect.top).toFixed(1)}px`)
      s.setProperty('--g-u', ((px - rect.left) / rect.width).toFixed(4))
      s.setProperty('--g-v', rect.height > 0 ? ((py - rect.top) / rect.height).toFixed(4) : '0.5')
      if (!lit.has(el)) {
        s.setProperty('--g-spot', '1')
        lit.add(el)
      }
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
    if (!hasPointer) setActive(true)
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
    for (const el of lit) el.style.setProperty('--g-spot', '0')
    lit.clear()
  }
}

// Nuxt registers every file under `components/` as a component; a default export
// keeps that registration well-typed. Import the named function.
export default startLightEngine
