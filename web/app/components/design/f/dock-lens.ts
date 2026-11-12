/**
 * Variant F ("Dock") — the dock's magnification, macOS style.
 *
 * Each tile grows with a Gaussian of its distance to the cursor (rest
 * positions, so the growth never feeds back into the measure), the slots spread
 * apart by exactly the width the tiles gained, and the shelf behind them widens
 * by the same amount — all with `transform`, nothing that touches layout.
 *
 * Geometry is read once on `pointerenter` (`offsetLeft` / `offsetWidth`, which
 * ignore transforms); each frame reads one rect (the list's, to place the
 * cursor) before writing. The values ease toward their targets in a rAF loop
 * that stops as soon as they settle, so a resting dock costs nothing.
 */

/** Extra scale right under the cursor: 1 → 1 + GAIN. */
const GAIN = 0.62
/** Falloff, in tile pitches (one sigma). */
const SIGMA = 1.1
/** Easing time constant, ms. */
const TAU = 50

export interface DockLensOptions {
  list: HTMLElement
  shelf: HTMLElement
}

interface Slot {
  el: HTMLElement
  tile: HTMLElement | null
  label: HTMLElement | null
  center: number
  width: number
  height: number
  scalable: boolean
  scale: number
  target: number
}

export function startDockLens({ list, shelf }: DockLensOptions): () => void {
  let slots: Slot[] = []
  let pitch = 52
  let shelfWidth = 1
  let clientX = 0
  let inside = false
  let frame = 0
  let last = 0
  let running = false
  let near: Slot | null = null

  function measure() {
    const els = Array.from(list.querySelectorAll<HTMLElement>('[data-dock-slot]'))
    const previous = new Map(slots.map(slot => [slot.el, slot.scale]))
    slots = els.map((el) => {
      const tile = el.querySelector<HTMLElement>('[data-dock-tile]')
      return {
        el,
        tile,
        label: el.querySelector<HTMLElement>('[data-dock-label]'),
        center: el.offsetLeft + el.offsetWidth / 2,
        width: tile?.offsetWidth ?? el.offsetWidth,
        height: tile?.offsetHeight ?? el.offsetHeight,
        scalable: !el.hasAttribute('data-dock-sep') && tile !== null,
        scale: previous.get(el) ?? 1,
        target: 1,
      }
    })
    const tiles = slots.filter(slot => slot.scalable)
    if (tiles.length > 1) pitch = (tiles.at(-1)!.center - tiles[0]!.center) / (tiles.length - 1)
    shelfWidth = Math.max(1, shelf.offsetWidth)
  }

  function setNear(next: Slot | null) {
    if (near === next) return
    near?.el.removeAttribute('data-near')
    near = next
    near?.el.setAttribute('data-near', '')
  }

  function schedule() {
    if (frame) return
    // A fresh run measures its first step from now; a running one from its last frame.
    if (!running) last = performance.now()
    frame = requestAnimationFrame(tick)
  }

  function tick(now: number) {
    frame = 0
    const dt = Math.min(48, Math.max(1, now - last))
    last = now
    const k = 1 - Math.exp(-dt / TAU)

    // Read: where the cursor is in the list's own (untransformed) coordinates.
    const rect = list.getBoundingClientRect()
    const unscale = rect.width > 0 ? list.offsetWidth / rect.width : 1
    const x = (clientX - rect.left) * unscale
    const sigma = SIGMA * pitch

    let nearest: Slot | null = null
    let nearestDistance = Infinity
    let moving = false
    for (const slot of slots) {
      if (!slot.scalable) continue
      const d = Math.abs(x - slot.center)
      slot.target = inside ? 1 + GAIN * Math.exp(-(d * d) / (2 * sigma * sigma)) : 1
      if (inside && d < nearestDistance) {
        nearestDistance = d
        nearest = slot
      }
      const delta = slot.target - slot.scale
      if (Math.abs(delta) < 0.0015) slot.scale = slot.target
      else {
        slot.scale += delta * k
        moving = true
      }
    }

    // Write: spread the slots by what each tile gained, centred on the dock.
    let gained = 0
    for (const slot of slots) gained += (slot.scale - 1) * slot.width
    let before = 0
    for (const slot of slots) {
      const extra = (slot.scale - 1) * slot.width
      const offset = -gained / 2 + before + extra / 2
      before += extra
      slot.el.style.transform = Math.abs(offset) < 0.05 ? '' : `translate3d(${offset.toFixed(2)}px, 0, 0)`
      if (slot.tile) slot.tile.style.transform = slot.scale === 1 ? '' : `scale(${slot.scale.toFixed(4)})`
      if (slot.label) {
        const lift = (slot.scale - 1) * slot.height
        slot.label.style.transform = lift < 0.05 ? '' : `translate3d(0, ${(-lift).toFixed(2)}px, 0)`
      }
    }
    shelf.style.transform = gained < 0.05 ? '' : `scaleX(${((shelfWidth + gained) / shelfWidth).toFixed(4)})`

    setNear(inside && nearestDistance < pitch * 0.55 ? nearest : null)
    running = moving
    if (moving) schedule()
  }

  function onEnter(event: PointerEvent) {
    if (event.pointerType === 'touch') return
    measure()
    inside = true
    clientX = event.clientX
    schedule()
  }

  function onMove(event: PointerEvent) {
    if (event.pointerType === 'touch') return
    if (!inside) {
      onEnter(event)
      return
    }
    clientX = event.clientX
    schedule()
  }

  function onLeave() {
    inside = false
    setNear(null)
    schedule()
  }

  list.addEventListener('pointerenter', onEnter)
  list.addEventListener('pointermove', onMove, { passive: true })
  list.addEventListener('pointerleave', onLeave)
  window.addEventListener('blur', onLeave)

  return () => {
    cancelAnimationFrame(frame)
    list.removeEventListener('pointerenter', onEnter)
    list.removeEventListener('pointermove', onMove)
    list.removeEventListener('pointerleave', onLeave)
    window.removeEventListener('blur', onLeave)
    setNear(null)
    for (const slot of slots) {
      slot.el.style.transform = ''
      if (slot.tile) slot.tile.style.transform = ''
      if (slot.label) slot.label.style.transform = ''
    }
    shelf.style.transform = ''
  }
}

// Nuxt registers every file under `components/` as a component; a default export
// keeps that registration well-typed. Import the named function.
export default startDockLens
