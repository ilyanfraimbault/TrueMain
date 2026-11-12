/**
 * Variant H — the motion behind the champion ring: one number, `pos`, the
 * ring's rotation in cards (0 = card 0 in front, 2.5 = halfway between cards 2
 * and 3). It loops: the caller wraps each card's offset into the ring.
 *
 * Four ways to turn it, one spring to settle it:
 * - drag: `pos` follows the pointer 1:1, then the release velocity is thrown
 *   ahead and rounded to a card (a flick always advances at least one);
 * - wheel: horizontal wheel/trackpad deltas move it directly, and it snaps
 *   once the gesture (momentum included) has been quiet for a moment;
 * - `go(±1)` / `goTo(i)`: buttons, keys, a click on a side card.
 *
 * A damped spring (slightly under-damped, so it lands with a little give) runs
 * in rAF only while it has somewhere to go; at rest nothing runs.
 */

export interface RingEngineOptions {
  count: number
  /** Apply the ring's rotation to the DOM. Called at most once per frame. */
  render: (pos: number) => void
  /** The card in front (rounded `pos`, wrapped) changed. */
  onActive: (index: number) => void
  /** The ring started moving / came to rest. */
  onMoving: (moving: boolean) => void
  reducedMotion: boolean
}

export interface RingEngine {
  readonly pos: number
  readonly moving: boolean
  setCount: (count: number) => void
  go: (delta: number) => void
  goTo: (index: number) => void
  /** Spin in from an offset (the entrance). */
  spinFrom: (offset: number) => void
  dragStart: () => void
  /** `deltaCards` since the drag started (positive turns the ring forward). */
  dragMove: (deltaCards: number, now: number) => void
  dragEnd: (cancelled: boolean) => void
  wheel: (deltaCards: number) => void
  destroy: () => void
}

const STIFFNESS = 140
const DAMPING = 2 * 0.78 * Math.sqrt(STIFFNESS)
const WHEEL_IDLE_MS = 140
/** How far a release velocity (cards/s) is thrown ahead, in seconds. */
const THROW = 0.2
const MAX_THROW = 3

export function wrapOffset(value: number, count: number): number {
  if (count <= 0) return 0
  const m = ((value % count) + count) % count
  return m > count / 2 ? m - count : m
}

export function createRingEngine(options: RingEngineOptions): RingEngine {
  let count = Math.max(1, options.count)
  let pos = 0
  let vel = 0
  let target = 0
  let mode: 'idle' | 'spring' | 'drag' | 'wheel' = 'idle'
  let frame = 0
  let renderFrame = 0
  let last = 0
  let activeIndex = 0
  let isMoving = false
  let dragOrigin = 0
  let wheelTimer: ReturnType<typeof setTimeout> | undefined
  let wheelDirection = 0
  const samples: { t: number, p: number }[] = []

  const wrapIndex = (value: number) => ((Math.round(value) % count) + count) % count

  function setMoving(next: boolean) {
    if (isMoving === next) return
    isMoving = next
    options.onMoving(next)
  }

  function publish() {
    options.render(pos)
    const index = wrapIndex(pos)
    if (index !== activeIndex) {
      activeIndex = index
      options.onActive(index)
    }
  }

  function scheduleRender() {
    if (renderFrame) return
    renderFrame = requestAnimationFrame(() => {
      renderFrame = 0
      publish()
    })
  }

  function rest() {
    cancelAnimationFrame(frame)
    frame = 0
    mode = 'idle'
    // Keep the numbers small: the ring looks the same one turn later.
    pos = ((target % count) + count) % count
    target = pos
    vel = 0
    publish()
    setMoving(false)
  }

  function step(now: number) {
    frame = 0
    const dt = Math.min(0.1, Math.max(0.001, (now - last) / 1000))
    last = now
    const n = Math.ceil(dt / 0.004)
    const h = dt / n
    for (let i = 0; i < n; i++) {
      vel += (STIFFNESS * (target - pos) - DAMPING * vel) * h
      pos += vel * h
    }
    if (Math.abs(target - pos) < 0.0015 && Math.abs(vel) < 0.01) {
      rest()
      return
    }
    publish()
    frame = requestAnimationFrame(step)
  }

  function springTo(next: number) {
    target = next
    if (options.reducedMotion) {
      pos = target
      vel = 0
      rest()
      return
    }
    mode = 'spring'
    setMoving(true)
    if (!frame) {
      last = performance.now()
      frame = requestAnimationFrame(step)
    }
  }

  function stopSpring() {
    cancelAnimationFrame(frame)
    frame = 0
    vel = 0
  }

  return {
    get pos() {
      return pos
    },
    get moving() {
      return isMoving
    },
    setCount(next) {
      count = Math.max(1, next)
      stopSpring()
      pos = 0
      target = 0
      activeIndex = 0
      mode = 'idle'
      publish()
      setMoving(false)
    },
    go(delta) {
      // Several presses in a row add up from where the ring is heading.
      const base = mode === 'spring' ? target : Math.round(pos)
      clearTimeout(wheelTimer)
      springTo(base + delta)
    },
    goTo(index) {
      const base = mode === 'spring' ? target : pos
      clearTimeout(wheelTimer)
      springTo(Math.round(base + wrapOffset(index - base, count)))
    },
    spinFrom(offset) {
      if (options.reducedMotion) return
      pos = offset
      target = 0
      vel = 0
      publish()
      springTo(0)
    },
    dragStart() {
      stopSpring()
      clearTimeout(wheelTimer)
      mode = 'drag'
      dragOrigin = pos
      samples.length = 0
      setMoving(true)
    },
    dragMove(deltaCards, now) {
      if (mode !== 'drag') return
      pos = dragOrigin + deltaCards
      samples.push({ t: now, p: pos })
      while (samples.length > 2 && now - samples[0]!.t > 90) samples.shift()
      scheduleRender()
    },
    dragEnd(cancelled) {
      if (mode !== 'drag') return
      cancelAnimationFrame(renderFrame)
      renderFrame = 0
      let velocity = 0
      const first = samples[0]
      const lastSample = samples[samples.length - 1]
      if (!cancelled && first && lastSample && lastSample.t - first.t > 8 && performance.now() - lastSample.t < 120) {
        velocity = ((lastSample.p - first.p) / (lastSample.t - first.t)) * 1000
      }
      const from = Math.round(dragOrigin)
      let next = Math.round(pos + velocity * THROW)
      // A flick advances at least one card, however short the drag was.
      if (next === from && Math.abs(velocity) > 1.1) next = from + Math.sign(velocity)
      next = Math.max(from - MAX_THROW, Math.min(from + MAX_THROW, next))
      vel = velocity
      springTo(next)
    },
    wheel(deltaCards) {
      if (mode === 'spring' || mode === 'idle') stopSpring()
      mode = 'wheel'
      setMoving(true)
      pos += deltaCards
      if (deltaCards !== 0) wheelDirection = Math.sign(deltaCards)
      scheduleRender()
      clearTimeout(wheelTimer)
      wheelTimer = setTimeout(() => {
        cancelAnimationFrame(renderFrame)
        renderFrame = 0
        // Lean toward the way the gesture went: a short swipe still turns a card.
        springTo(Math.round(pos + wheelDirection * 0.28))
      }, WHEEL_IDLE_MS)
    },
    destroy() {
      cancelAnimationFrame(frame)
      cancelAnimationFrame(renderFrame)
      clearTimeout(wheelTimer)
    },
  }
}

// Nuxt registers every file under `components/` as a component; a default
// export keeps that registration well-typed. Import the named function.
export default createRingEngine
