/**
 * Variant F ("Dock") — the champion wall's lens.
 *
 * The icon under the cursor swells and its neighbours a little less, pushed
 * gently outward so the swollen one has room: a fisheye over the grid. Each
 * tile's scale and push follow a Gaussian of its distance to the cursor,
 * measured from the tiles' rest centres (`offsetLeft` / `offsetTop`, which
 * ignore transforms), so the effect never feeds back into its own measure.
 *
 * One pointer listener on the stage, one rAF; per frame one rect read (the
 * stage, to place the cursor) before the transform writes, and only tiles whose
 * values changed are written. Geometry is cached on `pointerenter` and on
 * `refresh()` (filter, resize). The loop stops once every tile has settled.
 */

const GAIN = 0.4
/** Falloff, in tile pitches. */
const SIGMA = 0.64
/** Strongest outward push, px, felt one sigma away from the cursor. */
const PUSH = 7
const TAU = 45

export interface WallLensOptions {
  stage: HTMLElement
  /** The tile under the cursor (its `data-id`), or null when the cursor leaves. */
  onHover: (id: number | null) => void
}

interface Tile {
  li: HTMLElement
  link: HTMLElement
  cx: number
  cy: number
  s: number
  x: number
  y: number
  ts: number
  tx: number
  ty: number
}

export interface WallLens {
  refresh: () => void
  stop: () => void
}

export function startWallLens({ stage, onHover }: WallLensOptions): WallLens {
  let tiles: Tile[] = []
  let sigma = 40
  let clientX = 0
  let clientY = 0
  let inside = false
  let frame = 0
  let last = 0
  let running = false
  let hitTest = false
  let hovered: HTMLElement | null = null

  function measure() {
    const previous = new Map(tiles.map(tile => [tile.li, tile]))
    const next: Tile[] = []
    for (const li of stage.querySelectorAll<HTMLElement>('[data-tile]')) {
      const link = li.firstElementChild as HTMLElement | null
      if (!link || li.offsetParent === null) continue
      const old = previous.get(li)
      next.push({
        li,
        link,
        cx: li.offsetLeft + li.offsetWidth / 2,
        cy: li.offsetTop + li.offsetHeight / 2,
        s: old?.s ?? 1,
        x: old?.x ?? 0,
        y: old?.y ?? 0,
        ts: 1,
        tx: 0,
        ty: 0,
      })
      previous.delete(li)
    }
    // Tiles the filter just hid: drop their transforms.
    for (const tile of previous.values()) tile.link.style.transform = ''
    tiles = next
    const first = tiles[0]
    const second = tiles[1]
    const pitch = first && second && Math.abs(second.cy - first.cy) < 1
      ? second.cx - first.cx
      : (first?.li.offsetWidth ?? 56) * 1.18
    sigma = SIGMA * pitch
  }

  function setHovered(li: HTMLElement | null) {
    if (hovered === li) return
    if (hovered) delete hovered.dataset.lens
    hovered = li
    if (hovered) hovered.dataset.lens = 'on'
    const id = li ? Number(li.dataset.id) : null
    onHover(Number.isFinite(id) ? id : null)
  }

  function schedule() {
    if (frame) return
    if (!running) last = performance.now()
    frame = requestAnimationFrame(tick)
  }

  function tick(now: number) {
    frame = 0
    const dt = Math.min(48, Math.max(1, now - last))
    last = now
    const k = 1 - Math.exp(-dt / TAU)

    if (hitTest) {
      hitTest = false
      const under = document.elementFromPoint(clientX, clientY)?.closest<HTMLElement>('[data-tile]') ?? null
      if (under && stage.contains(under)) setHovered(under)
    }

    const rect = stage.getBoundingClientRect()
    const px = clientX - rect.left
    const py = clientY - rect.top
    const twoSigma2 = 2 * sigma * sigma
    const cutoff = (3 * sigma) ** 2
    let moving = false

    for (const tile of tiles) {
      if (inside) {
        const dx = tile.cx - px
        const dy = tile.cy - py
        const d2 = dx * dx + dy * dy
        if (d2 < cutoff) {
          const f = Math.exp(-d2 / twoSigma2)
          const d = Math.sqrt(d2)
          const push = d > 0.5 ? PUSH * (d / sigma) * Math.exp(0.5 - d2 / twoSigma2) : 0
          tile.ts = 1 + GAIN * f
          tile.tx = d > 0.5 ? (dx / d) * push : 0
          tile.ty = d > 0.5 ? (dy / d) * push : 0
        }
        else {
          tile.ts = 1
          tile.tx = 0
          tile.ty = 0
        }
      }
      else {
        tile.ts = 1
        tile.tx = 0
        tile.ty = 0
      }

      if (tile.s === tile.ts && tile.x === tile.tx && tile.y === tile.ty) continue
      const ds = tile.ts - tile.s
      const dxv = tile.tx - tile.x
      const dyv = tile.ty - tile.y
      if (Math.abs(ds) < 0.002 && Math.abs(dxv) < 0.08 && Math.abs(dyv) < 0.08) {
        tile.s = tile.ts
        tile.x = tile.tx
        tile.y = tile.ty
      }
      else {
        tile.s += ds * k
        tile.x += dxv * k
        tile.y += dyv * k
        moving = true
      }
      tile.link.style.transform = tile.s === 1 && tile.x === 0 && tile.y === 0
        ? ''
        : `translate3d(${tile.x.toFixed(2)}px, ${tile.y.toFixed(2)}px, 0) scale(${tile.s.toFixed(4)})`
    }

    running = moving
    if (moving) schedule()
  }

  function onEnter(event: PointerEvent) {
    if (event.pointerType === 'touch') return
    measure()
    inside = true
    clientX = event.clientX
    clientY = event.clientY
    schedule()
  }

  function onMove(event: PointerEvent) {
    if (event.pointerType === 'touch') return
    if (!inside) {
      onEnter(event)
    }
    clientX = event.clientX
    clientY = event.clientY
    const li = (event.target as Element | null)?.closest<HTMLElement>('[data-tile]') ?? null
    // Between two tiles the last one stays hovered: no flicker crossing a gap.
    if (li) setHovered(li)
    schedule()
  }

  function onLeave() {
    inside = false
    setHovered(null)
    schedule()
  }

  function onScroll() {
    if (!inside) return
    hitTest = true
    schedule()
  }

  stage.addEventListener('pointerenter', onEnter)
  stage.addEventListener('pointermove', onMove, { passive: true })
  stage.addEventListener('pointerleave', onLeave)
  window.addEventListener('scroll', onScroll, { passive: true })
  window.addEventListener('blur', onLeave)

  return {
    refresh() {
      if (!inside) return
      measure()
      schedule()
    },
    stop() {
      cancelAnimationFrame(frame)
      stage.removeEventListener('pointerenter', onEnter)
      stage.removeEventListener('pointermove', onMove)
      stage.removeEventListener('pointerleave', onLeave)
      window.removeEventListener('scroll', onScroll)
      window.removeEventListener('blur', onLeave)
      if (hovered) delete hovered.dataset.lens
      for (const tile of tiles) tile.link.style.transform = ''
      tiles = []
    },
  }
}

// Nuxt registers every file under `components/` as a component; a default export
// keeps that registration well-typed. Import the named function.
export default startWallLens
