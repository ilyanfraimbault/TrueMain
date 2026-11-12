<script setup lang="ts">
/**
 * Design exploration, variant C — a lens that trails the pointer over the
 * elements that ask for it (`data-c-cursor="Label"`): a small disc that
 * grows into a labelled badge ("View build") on the film frames. The native
 * cursor stays; this only annotates it. Fine pointers, `full` FX, motion
 * allowed — nothing is rendered otherwise.
 */
const fx = useFx()
const enabled = ref(false)
const label = ref('')
const active = ref(false)
const el = ref<HTMLElement | null>(null)
let teardown: (() => void) | null = null

onMounted(() => {
  if (!fx.value.finePointer || fx.value.level === 'lite' || fx.value.reducedMotion) return
  if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) return
  enabled.value = true

  const target = { x: -100, y: -100 }
  const pos = { x: -100, y: -100 }
  let raf = 0
  let seen = false

  function frame() {
    raf = 0
    pos.x += (target.x - pos.x) * 0.2
    pos.y += (target.y - pos.y) * 0.2
    if (el.value) el.value.style.transform = `translate3d(${pos.x.toFixed(1)}px, ${pos.y.toFixed(1)}px, 0)`
    if (Math.abs(target.x - pos.x) > 0.3 || Math.abs(target.y - pos.y) > 0.3) raf = requestAnimationFrame(frame)
  }

  function sync(element: Element | null) {
    const host = element?.closest?.('[data-c-cursor]') as HTMLElement | null
    const next = host?.dataset.cCursor ?? ''
    if (next && next !== label.value) label.value = next
    active.value = Boolean(host)
  }

  function onMove(event: PointerEvent) {
    if (event.pointerType === 'touch') return
    target.x = event.clientX
    target.y = event.clientY
    if (!seen) {
      seen = true
      pos.x = target.x
      pos.y = target.y
    }
    sync(event.target as Element | null)
    if (!raf) raf = requestAnimationFrame(frame)
  }

  // Scrolling moves the page under a still pointer (the film strip slides
  // sideways under it): hit-test again once per frame while it does.
  let scrollQueued = false
  function onScroll() {
    if (!seen || scrollQueued) return
    scrollQueued = true
    requestAnimationFrame(() => {
      scrollQueued = false
      sync(document.elementFromPoint(target.x, target.y))
    })
  }
  const onLeave = () => (active.value = false)

  window.addEventListener('pointermove', onMove, { passive: true })
  window.addEventListener('scroll', onScroll, { passive: true })
  document.documentElement.addEventListener('pointerleave', onLeave)
  teardown = () => {
    cancelAnimationFrame(raf)
    window.removeEventListener('pointermove', onMove)
    window.removeEventListener('scroll', onScroll)
    document.documentElement.removeEventListener('pointerleave', onLeave)
  }
})

onBeforeUnmount(() => teardown?.())
</script>

<template>
  <div
    v-if="enabled"
    ref="el"
    class="c-cursor"
    :class="{ 'is-on': active }"
    aria-hidden="true"
  >
    <span class="c-cursor__disc"><span class="c-cursor__label">{{ label }}</span></span>
  </div>
</template>
