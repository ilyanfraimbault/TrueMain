<script setup lang="ts">
// Variant A — a liquid-glass lens that drifts after the cursor across the hero,
// magnifying and bending whatever it passes over (the eclipse's corona, the
// headline). Rests on the orb's rim when the cursor is elsewhere. Chromium +
// full FX + a mouse only: without refraction a frosted disc over the headline
// would only blur it.
const props = defineProps<{ host: HTMLElement | null }>()

const SIZE = 168
const pos = reactive({ x: -9999, y: -9999 })
const shown = ref(false)
let target = { x: 0, y: 0 }
let inside = false
let frame = 0
let running = false

function rest() {
  const host = props.host
  if (!host) return { x: 0, y: 0 }
  const w = host.clientWidth
  const h = Math.min(host.clientHeight, window.innerHeight)
  return { x: w / 2 + Math.min(w * 0.3, h * 0.55) - SIZE / 2, y: h * 0.2 - SIZE / 2 }
}

function tick() {
  const k = inside ? 0.14 : 0.05
  pos.x += (target.x - pos.x) * k
  pos.y += (target.y - pos.y) * k
  if (Math.abs(target.x - pos.x) < 0.3 && Math.abs(target.y - pos.y) < 0.3) {
    running = false
    return
  }
  frame = requestAnimationFrame(tick)
}

function run() {
  if (running) return
  running = true
  frame = requestAnimationFrame(tick)
}

onMounted(() => {
  const host = props.host
  if (!host) return
  const start = rest()
  pos.x = start.x
  pos.y = start.y
  target = start
  const timer = window.setTimeout(() => (shown.value = true), 1100)

  const onMove = (e: PointerEvent) => {
    if (e.pointerType !== 'mouse') return
    const rect = host.getBoundingClientRect()
    const y = e.clientY - rect.top
    if (y > Math.min(rect.height, window.innerHeight)) return
    inside = true
    target = { x: e.clientX - rect.left - SIZE / 2, y: y - SIZE / 2 }
    run()
  }
  const onLeave = () => {
    inside = false
    target = rest()
    run()
  }
  // The host's size settles after mount (fonts, data); the rest spot follows it.
  const resizeObserver = new ResizeObserver(() => {
    if (inside) return
    target = rest()
    if (!shown.value) {
      pos.x = target.x
      pos.y = target.y
    }
    run()
  })
  resizeObserver.observe(host)
  host.addEventListener('pointermove', onMove, { passive: true })
  host.addEventListener('pointerleave', onLeave)
  onBeforeUnmount(() => {
    window.clearTimeout(timer)
    cancelAnimationFrame(frame)
    resizeObserver.disconnect()
    host.removeEventListener('pointermove', onMove)
    host.removeEventListener('pointerleave', onLeave)
  })
})
</script>

<template>
  <div
    aria-hidden="true"
    class="dsa-lens pointer-events-none absolute left-0 top-0 z-20"
    :class="{ 'dsa-lens--shown': shown }"
    :style="{ width: `${SIZE}px`, height: `${SIZE}px`, transform: `translate3d(${pos.x}px, ${pos.y}px, 0)` }"
  >
    <FxLiquidGlass
      :radius="999"
      :zoom="1.45"
      :bezel="46"
      :depth="30"
      :blur="0.2"
      :aberration="0.45"
      :saturation="1.35"
      class="size-full"
    />
  </div>
</template>
