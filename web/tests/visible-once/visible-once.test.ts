import { afterEach, describe, expect, it } from 'vitest'
import * as vue from 'vue'

/**
 * `useVisibleOnce` is the favorites page's fan-out bound (#872): each card's
 * profile + matches fetch waits until the card approaches the viewport, so a
 * 30-favorite list no longer fires ~60 requests in one tick.
 *
 * What is worth testing is the bookkeeping around the observer, not the
 * browser's geometry: the gate opens on the *first* intersection, latches (a
 * card scrolling back out must not undo a fetch), stops observing once open,
 * and degrades to open where `IntersectionObserver` does not exist — otherwise
 * such a browser would sit on skeletons forever.
 *
 * Same auto-import stand-in as `tests/truemain-fetch/client-only.test.ts`: the
 * composable is app code written against Nuxt's auto-imports, so the Vue
 * helpers have to be globals before the module is evaluated.
 */
Object.assign(globalThis, {
  ref: vue.ref,
  onMounted: vue.onMounted,
  onBeforeUnmount: vue.onBeforeUnmount,
  toValue: vue.toValue,
})

const { useVisibleOnce } = await import('~/composables/useVisibleOnce')

class FakeIntersectionObserver {
  static instances: FakeIntersectionObserver[] = []
  observed: unknown[] = []
  disconnectCount = 0

  constructor(
    readonly callback: (entries: Array<{ isIntersecting: boolean }>) => void,
    readonly options?: { rootMargin?: string },
  ) {
    FakeIntersectionObserver.instances.push(this)
  }

  observe(target: unknown) {
    this.observed.push(target)
  }

  unobserve() {}

  disconnect() {
    this.disconnectCount++
  }

  /** Stand in for the browser reporting on the observed element. */
  emit(isIntersecting: boolean) {
    this.callback([{ isIntersecting }])
  }
}

const originalObserver = globalThis.IntersectionObserver

function installObserver() {
  FakeIntersectionObserver.instances = []
  Object.defineProperty(globalThis, 'IntersectionObserver', {
    value: FakeIntersectionObserver,
    configurable: true,
    writable: true,
  })
  return FakeIntersectionObserver.instances
}

function removeObserver() {
  Object.defineProperty(globalThis, 'IntersectionObserver', {
    value: undefined,
    configurable: true,
    writable: true,
  })
}

afterEach(() => {
  Object.defineProperty(globalThis, 'IntersectionObserver', {
    value: originalObserver,
    configurable: true,
    writable: true,
  })
})

/** A card-shaped component: one root element, gated content inside it. */
function mount(options?: { rootMargin?: string }) {
  const el = vue.ref<HTMLElement | null>(null)
  let visible!: vue.Ref<boolean>

  const component = vue.defineComponent({
    setup() {
      visible = useVisibleOnce(el, options)
      return () => vue.h('section', { ref: el }, visible.value ? 'live' : 'gated')
    },
  })

  const app = vue.createApp(component)
  app.mount(document.createElement('div'))
  return { app, el, visible }
}

describe('useVisibleOnce', () => {
  it('observes the root element and stays closed until it intersects', () => {
    const instances = installObserver()
    const { el, visible } = mount()

    expect(instances).toHaveLength(1)
    expect(instances[0]!.observed).toEqual([el.value])
    expect(visible.value).toBe(false)
  })

  it('ignores a non-intersecting report', () => {
    const instances = installObserver()
    const { visible } = mount()

    instances[0]!.emit(false)
    expect(visible.value).toBe(false)
    expect(instances[0]!.disconnectCount).toBe(0)
  })

  it('opens on the first intersection and stops observing', () => {
    const instances = installObserver()
    const { visible } = mount()

    instances[0]!.emit(true)
    expect(visible.value).toBe(true)
    expect(instances[0]!.disconnectCount).toBe(1)
  })

  it('latches — scrolling back out must not undo the work it unlocked', () => {
    const instances = installObserver()
    const { visible } = mount()

    instances[0]!.emit(true)
    instances[0]!.emit(false)
    expect(visible.value).toBe(true)
  })

  it('pre-loads slightly ahead of the viewport, and takes an override', () => {
    const instances = installObserver()
    mount()
    expect(instances[0]!.options?.rootMargin).toBe('200px')

    mount({ rootMargin: '600px' })
    expect(instances[1]!.options?.rootMargin).toBe('600px')
  })

  it('disconnects on unmount', () => {
    const instances = installObserver()
    const { app } = mount()

    app.unmount()
    expect(instances[0]!.disconnectCount).toBe(1)
  })

  it('opens immediately where the observer does not exist', () => {
    removeObserver()
    const { visible } = mount()
    expect(visible.value).toBe(true)
  })
})
