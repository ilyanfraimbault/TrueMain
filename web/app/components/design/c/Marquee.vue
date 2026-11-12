<script setup lang="ts">
/**
 * Design exploration, variant C — a band of champion names in giant type,
 * sliding past like end credits. Scrolling the page pushes it along and leans
 * it into the motion (speed and skew follow the scroll velocity), then it eases
 * back to its idle drift.
 *
 * Decorative and non-interactive (`aria-hidden`): the same champions are links
 * in the chapters around it. The loop runs only while the band is on screen;
 * `lite` falls back to a plain CSS loop, reduced motion holds it still.
 */
const props = defineProps<{
  champions: { id: number, name: string, iconUrl: string }[]
}>()

const fx = useFx()
const iconOf = useCanonicalIcon()
const root = ref<HTMLElement | null>(null)
const track = ref<HTMLElement | null>(null)
const mode = ref<'js' | 'css' | 'static'>('css')

// Two identical runs, so a shift of one run's width loops seamlessly. Each run
// repeats the list until it is comfortably wider than any screen.
const run = computed(() => {
  const list = props.champions
  if (list.length === 0) return []
  const repeats = Math.max(1, Math.ceil(14 / list.length))
  return Array.from({ length: repeats }, (_, r) => list.map(c => ({ ...c, key: `${r}-${c.id}` }))).flat()
})

let teardown: (() => void) | null = null

onMounted(() => {
  if (fx.value.reducedMotion || window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
    mode.value = 'static'
    return
  }
  if (fx.value.level === 'lite') {
    mode.value = 'css'
    return
  }
  mode.value = 'js'

  let raf = 0
  let visible = false
  let offset = 0
  let runWidth = 0
  let lastY = window.scrollY
  let velocity = 0
  let last = 0

  const measure = () => {
    const first = track.value?.firstElementChild as HTMLElement | null
    runWidth = first?.offsetWidth ?? 0
  }

  function frame(now: number) {
    raf = requestAnimationFrame(frame)
    const dt = last ? Math.min(3, (now - last) / 16.667) : 1
    last = now
    const y = window.scrollY
    const dy = y - lastY
    lastY = y
    // Smoothed scroll velocity (px per frame), decaying to rest.
    velocity += (dy / dt - velocity) * 0.18
    const speed = 0.9 + Math.min(40, Math.abs(velocity) * 0.9)
    offset -= speed * dt * (velocity < -0.5 ? -1 : 1)
    if (runWidth > 0) {
      offset %= runWidth
      if (offset > 0) offset -= runWidth
    }
    const skew = Math.max(-9, Math.min(9, velocity * -0.35))
    if (track.value) track.value.style.transform = `translate3d(${offset.toFixed(2)}px,0,0) skewX(${skew.toFixed(2)}deg)`
  }

  const start = () => {
    if (raf || !visible || document.hidden) return
    last = 0
    lastY = window.scrollY
    raf = requestAnimationFrame(frame)
  }
  const stop = () => {
    cancelAnimationFrame(raf)
    raf = 0
  }

  const io = new IntersectionObserver(([entry]) => {
    visible = entry?.isIntersecting ?? false
    if (visible) {
      measure()
      start()
    }
    else {
      stop()
    }
  })
  if (root.value) io.observe(root.value)
  const ro = new ResizeObserver(measure)
  if (track.value) ro.observe(track.value)
  const onVisibility = () => (document.hidden ? stop() : start())
  document.addEventListener('visibilitychange', onVisibility)

  teardown = () => {
    stop()
    io.disconnect()
    ro.disconnect()
    document.removeEventListener('visibilitychange', onVisibility)
  }
})

onBeforeUnmount(() => teardown?.())
</script>

<template>
  <div
    ref="root"
    class="c-marquee"
    :class="`is-${mode}`"
    aria-hidden="true"
  >
    <div class="c-marquee__rule" />
    <div class="c-marquee__viewport">
      <div
        ref="track"
        class="c-marquee__track"
      >
        <div
          v-for="copy in 2"
          :key="copy"
          class="c-marquee__run"
        >
          <span
            v-for="(champion, index) in run"
            :key="champion.key"
            class="c-marquee__item"
            :class="{ 'is-outline': index % 2 === 1 }"
          >
            <span class="c-marquee__name">{{ champion.name }}</span>
            <img
              v-if="champion.iconUrl"
              :src="iconOf(champion.iconUrl)"
              alt=""
              width="64"
              height="64"
              loading="lazy"
              decoding="async"
              class="c-marquee__icon"
            >
          </span>
        </div>
      </div>
    </div>
    <div class="c-marquee__rule" />
  </div>
</template>
