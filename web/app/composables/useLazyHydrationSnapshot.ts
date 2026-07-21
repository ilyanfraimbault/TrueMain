/**
 * Freezes a prop bundle passed to a `hydrate-on-visible` lazy child at its
 * SSR-matching `initial` value until the child actually mounts, then reveals
 * `live()`'s current (and from then on reactive) value.
 *
 * Why: the champion detail pages' chart/panel data is deliberately
 * client-only (`server: false`), so SSR always renders each lazy child in
 * its loading/empty state. `hydrate-on-visible` defers a child's hydration
 * until it scrolls into view — by then the client-only fetch has long since
 * resolved, so hydrating with the *live* value reconciles Vue's render
 * against a DOM that no longer matches, producing a hydration mismatch
 * (#834/#837). Passing this frozen bundle keeps the child's first
 * (hydration) render identical to what SSR produced; `@vue:mounted="reveal"`
 * on the child then swaps in the live, reactive value as a normal
 * post-hydration update, not part of hydration reconciliation.
 */
export function useLazyHydrationSnapshot<T extends object>(initial: T, live: () => T) {
  const revealed = ref(false)
  const value = computed(() => (revealed.value ? live() : initial))
  function reveal() {
    revealed.value = true
  }
  // Wrapped in `reactive()` so `snapshot.value` auto-unwraps the computed ref
  // in both script and `v-bind="snapshot.value"` template usage — a plain
  // `{ value, reveal }` object would hand templates the raw ComputedRef
  // instead of its unwrapped data (nested ref access isn't auto-unwrapped).
  return reactive({ value, reveal })
}
