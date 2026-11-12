/**
 * Variant H ("Glass Arena") — lit edges. Every `.surface` and `[data-h-spot]`
 * element near the cursor gets `--h-mx` / `--h-my` (px, element-relative),
 * `--h-u` / `--h-v` (0..1 fractions of its box) and `--h-spot` (0/1). The CSS
 * paints a rim light there, so a panel's border catches the light before the
 * cursor even reaches it. No page-wide glow: only edges light up.
 *
 * The variables are registered as non-inherited (`design-h.css`), so a write
 * restyles one element, never its subtree. Reads (rects) happen in one pass and
 * writes in the next, inside one rAF; pointer and scroll handlers only schedule
 * that frame, so nothing runs while the cursor rests. Off-screen targets are
 * skipped (IntersectionObserver) and a MutationObserver marks the target list
 * stale when the page changes.
 */

/** How far from an element's box the cursor still lights it (px). */
const NEAR = 320
const SELECTOR = '.surface, [data-h-spot]'

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

    const rects: [HTMLElement, DOMRect][] = []
    for (const el of new Set([...visible, ...lit])) rects.push([el, el.getBoundingClientRect()])

    for (const [el, rect] of rects) {
      const near = hasPointer && rect.width > 0 && distanceToRect(rect, px, py) < NEAR
      if (!near) {
        // Leave the light where it was; `--h-spot` easing to 0 fades it there.
        if (lit.has(el)) {
          el.style.setProperty('--h-spot', '0')
          lit.delete(el)
        }
        continue
      }
      const s = el.style
      s.setProperty('--h-mx', `${(px - rect.left).toFixed(1)}px`)
      s.setProperty('--h-my', `${(py - rect.top).toFixed(1)}px`)
      s.setProperty('--h-u', ((px - rect.left) / rect.width).toFixed(4))
      s.setProperty('--h-v', rect.height > 0 ? ((py - rect.top) / rect.height).toFixed(4) : '0.5')
      if (!lit.has(el)) {
        s.setProperty('--h-spot', '1')
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
    setActive(true)
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
    for (const el of lit) el.style.setProperty('--h-spot', '0')
    lit.clear()
  }
}

// Nuxt registers every file under `components/` as a component; a default
// export keeps that registration well-typed. Import the named function.
export default startLightEngine
