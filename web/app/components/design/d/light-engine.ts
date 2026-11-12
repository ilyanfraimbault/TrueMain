/**
 * Variant D ("Cockpit") — the one pointer listener behind every lit edge.
 *
 * Adapted from variant B's spotlight engine, minus the page-wide torch and its
 * dot texture: there is no light of its own here, only the edges of the panels
 * the cursor comes near. Every `.surface` and every `[data-d-spot]` element
 * within `NEAR` px of the cursor gets:
 *
 *   --d-mx / --d-my   cursor position, px, relative to the element's box
 *   --d-u  / --d-v    the same as 0..1 fractions of the box
 *   --d-spot          0/1, eased by a CSS transition (design-d.css)
 *
 * Those are registered as non-inherited properties, so a write restyles the
 * element alone, never its subtree.
 *
 * Nothing runs while the cursor rests: the pointer and scroll handlers only
 * schedule one rAF frame, which reads every rect first and writes after.
 * Off-screen targets are skipped (IntersectionObserver), and a MutationObserver
 * marks the target list stale when the page changes (a route, a skeleton
 * swapped for rows).
 */

/** How far from an element's box the cursor still lights it (px). */
const NEAR = 220
const SELECTOR = '.surface, [data-d-spot]'

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

  function tick() {
    frame = 0
    if (targetsDirty) refreshTargets()

    // Read every rect first, then write: no forced layout between the two.
    const rects: [HTMLElement, DOMRect][] = []
    for (const el of new Set([...visible, ...lit])) rects.push([el, el.getBoundingClientRect()])

    for (const [el, rect] of rects) {
      const near = hasPointer && rect.width > 0 && distanceToRect(rect, px, py) < NEAR
      if (!near) {
        // Leave the light where it was and let `--d-spot` fade it out there.
        if (lit.has(el)) {
          el.style.setProperty('--d-spot', '0')
          lit.delete(el)
        }
        continue
      }
      const s = el.style
      s.setProperty('--d-mx', `${(px - rect.left).toFixed(1)}px`)
      s.setProperty('--d-my', `${(py - rect.top).toFixed(1)}px`)
      s.setProperty('--d-u', ((px - rect.left) / rect.width).toFixed(4))
      s.setProperty('--d-v', rect.height > 0 ? ((py - rect.top) / rect.height).toFixed(4) : '0.5')
      if (!lit.has(el)) {
        s.setProperty('--d-spot', '1')
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
  window.addEventListener('scroll', onScroll, { passive: true, capture: true })
  window.addEventListener('blur', onBlur)
  document.addEventListener('mouseout', onMouseOut)

  return () => {
    cancelAnimationFrame(frame)
    window.removeEventListener('pointermove', onPointerMove)
    window.removeEventListener('scroll', onScroll, { capture: true })
    window.removeEventListener('blur', onBlur)
    document.removeEventListener('mouseout', onMouseOut)
    io.disconnect()
    mo.disconnect()
    for (const el of lit) el.style.setProperty('--d-spot', '0')
    lit.clear()
  }
}

// Nuxt registers every file under `components/` as a component; a default
// export keeps that registration well-typed. Import the named function.
export default startLightEngine
