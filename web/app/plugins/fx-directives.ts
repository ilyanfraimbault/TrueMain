import type { Directive, DirectiveBinding } from 'vue'

/**
 * Design exploration (prototype): the motion toolkit every variant draws on.
 *
 * - `v-reveal`            — fades/rises an element in when it scrolls into view.
 *                           Value: delay in ms, or `{ delay, effect }` where
 *                           `effect` is `rise` (default) | `blur` | `scale` |
 *                           `clip` | `left` | `right`. `.stagger` applies it to
 *                           every child with a growing delay (value = step ms).
 *                           Elements already on screen at mount are left alone
 *                           (no hide/show flash); `.load` plays them anyway.
 * - `v-spotlight`         — writes `--mx`/`--my` (px, element-relative) and
 *                           `--spot` (0/1) on pointer move. `.group` writes them
 *                           on every `[data-spot]` descendant instead (the
 *                           "light seen across a grid" effect). `.window`
 *                           listens on the window (cursor light layers).
 * - `v-tilt`              — 3D tilt toward the cursor: `--rx`/`--ry` (deg) and
 *                           `--gx`/`--gy` (% glare position). Value: max degrees.
 * - `v-magnetic`          — pulls the element toward the cursor: `--tx`/`--ty`.
 *                           Value: strength 0..1.
 *
 * The directives only write CSS variables and classes; how they look is each
 * variant's CSS (`assets/css/fx.css` holds neutral defaults). Pointer effects
 * skip touch input. Everything is skipped under reduced motion.
 */

type RevealValue = number | { delay?: number, effect?: string, step?: number } | undefined

let revealObserver: IntersectionObserver | null = null

function getRevealObserver(): IntersectionObserver {
  if (!revealObserver) {
    revealObserver = new IntersectionObserver((entries) => {
      for (const entry of entries) {
        if (!entry.isIntersecting) continue
        const el = entry.target as HTMLElement
        el.classList.add('fx-revealed')
        revealObserver!.unobserve(el)
      }
    }, { rootMargin: '0px 0px -10% 0px', threshold: 0.08 })
  }
  return revealObserver
}

function reducedMotion(): boolean {
  return window.matchMedia('(prefers-reduced-motion: reduce)').matches
}

function prepareReveal(el: HTMLElement, delay: number, effect: string | undefined, force: boolean) {
  if (el.dataset.revealPrepared) return
  el.dataset.revealPrepared = '1'
  const rect = el.getBoundingClientRect()
  const onScreen = rect.top < window.innerHeight * 0.95 && rect.bottom > 0
  if (onScreen && !force) return
  el.classList.add('fx-reveal')
  if (effect) el.dataset.reveal = effect
  el.style.setProperty('--reveal-delay', `${delay}ms`)
  if (onScreen) {
    // `.load`: play the entrance now, after the hidden state has painted once.
    requestAnimationFrame(() => requestAnimationFrame(() => el.classList.add('fx-revealed')))
  }
  else {
    getRevealObserver().observe(el)
  }
}

function applyReveal(el: HTMLElement, binding: DirectiveBinding<RevealValue>) {
  if (reducedMotion()) return
  const value = binding.value
  const options = typeof value === 'number' ? { delay: value } : (value ?? {})
  const force = binding.modifiers.load === true
  if (binding.modifiers.stagger) {
    // Children that arrive later (a skeleton swapped for real rows) are picked
    // up on update; the ones already prepared are skipped.
    const step = typeof value === 'number' ? value : (options.step ?? 70)
    const base = typeof value === 'object' ? (options.delay ?? 0) : 0
    Array.from(el.children).forEach((child, index) =>
      prepareReveal(child as HTMLElement, base + index * step, options.effect, force))
    return
  }
  prepareReveal(el, options.delay ?? 0, options.effect, force)
}

const reveal: Directive<HTMLElement, RevealValue> = {
  mounted: applyReveal,
  updated(el, binding) {
    if (binding.modifiers.stagger) applyReveal(el, binding)
  },
  unmounted(el) {
    revealObserver?.unobserve(el)
    if (el.children) for (const child of Array.from(el.children)) revealObserver?.unobserve(child)
  },
  getSSRProps() {
    return {}
  },
}

// ─── Pointer-driven directives ──────────────────────────────────────────────

interface PointerState {
  cleanup: () => void
}

const states = new WeakMap<HTMLElement, PointerState>()

function rafThrottle<T extends unknown[]>(fn: (...args: T) => void) {
  let frame = 0
  let lastArgs: T | null = null
  const run = () => {
    frame = 0
    if (lastArgs) fn(...lastArgs)
  }
  const throttled = (...args: T) => {
    lastArgs = args
    if (!frame) frame = requestAnimationFrame(run)
  }
  throttled.cancel = () => cancelAnimationFrame(frame)
  return throttled
}

function setVars(el: HTMLElement, clientX: number, clientY: number) {
  const rect = el.getBoundingClientRect()
  el.style.setProperty('--mx', `${(clientX - rect.left).toFixed(1)}px`)
  el.style.setProperty('--my', `${(clientY - rect.top).toFixed(1)}px`)
}

const spotlight: Directive<HTMLElement, undefined> = {
  mounted(el, binding: DirectiveBinding) {
    if (reducedMotion()) return
    const group = binding.modifiers.group === true
    const onWindow = binding.modifiers.window === true
    const target: HTMLElement | Window = onWindow ? window : el

    const update = rafThrottle((x: number, y: number) => {
      if (group) {
        el.querySelectorAll<HTMLElement>('[data-spot]').forEach(item => setVars(item, x, y))
      }
      else {
        setVars(el, x, y)
      }
    })
    const onMove = (event: Event) => {
      const e = event as PointerEvent
      if (e.pointerType === 'touch') return
      el.style.setProperty('--spot', '1')
      update(e.clientX, e.clientY)
    }
    const onLeave = () => el.style.setProperty('--spot', '0')
    target.addEventListener('pointermove', onMove, { passive: true })
    if (onWindow) document.documentElement.addEventListener('pointerleave', onLeave)
    else el.addEventListener('pointerleave', onLeave)
    states.set(el, {
      cleanup: () => {
        update.cancel()
        target.removeEventListener('pointermove', onMove)
        if (onWindow) document.documentElement.removeEventListener('pointerleave', onLeave)
        else el.removeEventListener('pointerleave', onLeave)
      },
    })
  },
  unmounted(el) {
    states.get(el)?.cleanup()
    states.delete(el)
  },
  getSSRProps() {
    return {}
  },
}

const tilt: Directive<HTMLElement, number | undefined> = {
  mounted(el, binding) {
    if (reducedMotion()) return
    const max = binding.value ?? 8
    el.classList.add('fx-tilt')
    const update = rafThrottle((x: number, y: number) => {
      const rect = el.getBoundingClientRect()
      const px = (x - rect.left) / rect.width
      const py = (y - rect.top) / rect.height
      el.style.setProperty('--rx', `${((0.5 - py) * max * 2).toFixed(2)}deg`)
      el.style.setProperty('--ry', `${((px - 0.5) * max * 2).toFixed(2)}deg`)
      el.style.setProperty('--gx', `${(px * 100).toFixed(1)}%`)
      el.style.setProperty('--gy', `${(py * 100).toFixed(1)}%`)
    })
    const onMove = (e: PointerEvent) => {
      if (e.pointerType === 'touch') return
      el.classList.add('fx-tilt-active')
      update(e.clientX, e.clientY)
    }
    const onLeave = () => {
      update.cancel()
      el.classList.remove('fx-tilt-active')
      el.style.setProperty('--rx', '0deg')
      el.style.setProperty('--ry', '0deg')
    }
    el.addEventListener('pointermove', onMove, { passive: true })
    el.addEventListener('pointerleave', onLeave)
    states.set(el, {
      cleanup: () => {
        update.cancel()
        el.removeEventListener('pointermove', onMove)
        el.removeEventListener('pointerleave', onLeave)
      },
    })
  },
  unmounted(el) {
    states.get(el)?.cleanup()
    states.delete(el)
  },
  getSSRProps() {
    return {}
  },
}

const magnetic: Directive<HTMLElement, number | undefined> = {
  mounted(el, binding) {
    if (reducedMotion()) return
    const strength = binding.value ?? 0.35
    el.classList.add('fx-magnetic')
    const update = rafThrottle((x: number, y: number) => {
      const rect = el.getBoundingClientRect()
      const dx = x - (rect.left + rect.width / 2)
      const dy = y - (rect.top + rect.height / 2)
      el.style.setProperty('--tx', `${(dx * strength).toFixed(1)}px`)
      el.style.setProperty('--ty', `${(dy * strength).toFixed(1)}px`)
    })
    const onMove = (e: PointerEvent) => {
      if (e.pointerType === 'touch') return
      update(e.clientX, e.clientY)
    }
    const onLeave = () => {
      update.cancel()
      el.style.setProperty('--tx', '0px')
      el.style.setProperty('--ty', '0px')
    }
    el.addEventListener('pointermove', onMove, { passive: true })
    el.addEventListener('pointerleave', onLeave)
    states.set(el, {
      cleanup: () => {
        update.cancel()
        el.removeEventListener('pointermove', onMove)
        el.removeEventListener('pointerleave', onLeave)
      },
    })
  },
  unmounted(el) {
    states.get(el)?.cleanup()
    states.delete(el)
  },
  getSSRProps() {
    return {}
  },
}

export default defineNuxtPlugin((nuxtApp) => {
  nuxtApp.vueApp.directive('reveal', reveal)
  nuxtApp.vueApp.directive('spotlight', spotlight)
  nuxtApp.vueApp.directive('tilt', tilt)
  nuxtApp.vueApp.directive('magnetic', magnetic)
})
