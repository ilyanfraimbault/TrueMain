/**
 * Variant F ("Dock") — lit edges. Adapted from variant B's spotlight engine with
 * the torch and the dot texture removed: there is no light on the page itself,
 * only on the edges of the panels the cursor comes near.
 *
 * Every `.surface` and every `[data-f-spot]` element within `NEAR` px of the
 * cursor gets `--f-mx` / `--f-my` (px, element-relative) and `--f-spot` (0/1).
 * They are registered as non-inherited properties (design-f.css), so a write
 * restyles that one element, never its subtree.
 *
 * Rects are read in one pass and the variables written in the next, inside one
 * rAF; the pointer and scroll handlers only schedule that frame, and nothing
 * runs while the cursor rests. Off-screen targets are skipped
 * (IntersectionObserver); a MutationObserver marks the target list stale when
 * the page changes.
 */

const NEAR = 320
const SELECTOR = '.surface, [data-f-spot]'

function distanceToRect(rect: DOMRect, x: number, y: number): number {
  const dx = Math.max(rect.left - x, 0, x - rect.right)
  const dy = Math.max(rect.top - y, 0, y - rect.bottom)
  return Math.hypot(dx, dy)
}

export function startEdgeLight(): () => void {
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

    const rects: [HTMLElement, DOMRect][] = []
    for (const el of new Set([...visible, ...lit])) rects.push([el, el.getBoundingClientRect()])

    for (const [el, rect] of rects) {
      const near = hasPointer && rect.width > 0 && distanceToRect(rect, px, py) < NEAR
      if (!near) {
        // The light stays where it was; `--f-spot` fades it out there.
        if (lit.delete(el)) el.style.setProperty('--f-spot', '0')
        continue
      }
      const s = el.style
      s.setProperty('--f-mx', `${(px - rect.left).toFixed(1)}px`)
      s.setProperty('--f-my', `${(py - rect.top).toFixed(1)}px`)
      if (!lit.has(el)) {
        s.setProperty('--f-spot', '1')
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

  return () => {
    cancelAnimationFrame(frame)
    window.removeEventListener('pointermove', onPointerMove)
    window.removeEventListener('scroll', onScroll)
    window.removeEventListener('blur', onBlur)
    document.removeEventListener('mouseout', onMouseOut)
    io.disconnect()
    mo.disconnect()
    for (const el of lit) el.style.setProperty('--f-spot', '0')
    lit.clear()
  }
}

// Nuxt registers every file under `components/` as a component; a default export
// keeps that registration well-typed. Import the named function.
export default startEdgeLight
