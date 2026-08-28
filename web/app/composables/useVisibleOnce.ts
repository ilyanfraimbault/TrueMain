import type { Ref } from 'vue'

/**
 * Latch a flag the first time `target` comes within `rootMargin` of the
 * viewport, then stop observing. One-way on purpose: work that was already
 * paid for (a fetch, a heavy chunk) must not be undone when the element
 * scrolls back out.
 *
 * This is the *client-rendered* counterpart of `hydrate-on-visible`. Nuxt's
 * lazy-hydration strategies hook Vue's `__asyncHydrate`, which the renderer
 * only calls while hydrating server markup — a subtree that is created
 * client-side (here: after the `localStorage` read in `onMounted`) mounts
 * immediately and its strategy is never consulted. So content that never
 * exists at SSR needs an explicit observer — the case this generalises from,
 * the homepage's item-map gate (`home/TruemainsPanel.vue`), which now calls it.
 *
 * Must be called from a component `setup()`: the observer is attached in
 * `onMounted`, both because the template ref is only populated by then and
 * because `IntersectionObserver` does not exist on the server.
 */
export function useVisibleOnce(
  target: MaybeRefOrGetter<HTMLElement | null | undefined>,
  options: { rootMargin?: string } = {},
): Ref<boolean> {
  const visible = ref(false)

  onMounted(() => {
    const el = toValue(target)
    // No element, or a browser/test environment without the observer: there is
    // no way to tell, so reveal rather than stall forever in the gated state.
    if (!el || typeof IntersectionObserver === 'undefined') {
      visible.value = true
      return
    }

    const observer = new IntersectionObserver((entries) => {
      if (!entries.some(entry => entry.isIntersecting)) return
      visible.value = true
      observer.disconnect()
    }, { rootMargin: options.rootMargin ?? '200px' })

    observer.observe(el)
    onBeforeUnmount(() => observer.disconnect())
  })

  return visible
}
