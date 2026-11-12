<script setup lang="ts">
/**
 * Design exploration (prototype): a number that counts up to its value the
 * first time it is on screen, and glides to any later value. Server-rendered at
 * its final value; reduced motion shows the value at once.
 */
const props = withDefaults(defineProps<{
  value: number
  format?: (value: number) => string
  duration?: number
}>(), {
  format: (value: number) => Math.round(value).toLocaleString('en-US'),
  duration: 1800,
})

const el = ref<HTMLElement | null>(null)
const shown = ref(props.value)
let frame = 0
let seen = false

function play(from: number, to: number) {
  cancelAnimationFrame(frame)
  if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
    shown.value = to
    return
  }
  const start = performance.now()
  const tick = (now: number) => {
    const t = Math.min(1, (now - start) / props.duration)
    const eased = t === 1 ? 1 : 1 - 2 ** (-10 * t)
    shown.value = from + (to - from) * eased
    if (t < 1) frame = requestAnimationFrame(tick)
  }
  frame = requestAnimationFrame(tick)
}

onMounted(() => {
  if (!el.value) return
  // On screen already: count now, so the figure is never left at 0 waiting
  // for an observer (a background tab never delivers one). Below the fold:
  // hold at 0 until it scrolls into view.
  const rect = el.value.getBoundingClientRect()
  if (rect.top < window.innerHeight && rect.bottom > 0) {
    seen = true
    play(0, props.value)
    onBeforeUnmount(() => cancelAnimationFrame(frame))
    return
  }
  shown.value = 0
  const observer = new IntersectionObserver(([entry]) => {
    if (!entry?.isIntersecting) return
    seen = true
    observer.disconnect()
    play(0, props.value)
  }, { threshold: 0.4 })
  observer.observe(el.value)
  onBeforeUnmount(() => {
    observer.disconnect()
    cancelAnimationFrame(frame)
  })
})

watch(() => props.value, (to) => {
  if (seen) play(shown.value, to)
})
</script>

<template>
  <span
    ref="el"
    class="tabular-nums"
  >{{ format(shown) }}</span>
</template>
