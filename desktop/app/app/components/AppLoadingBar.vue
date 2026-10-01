<script setup lang="ts">
/**
 * The window's loading bar (#1788): a 2 px primary line across the top edge,
 * over the champion search — the app has no header to hang the site's bar
 * under (`web/app/components/AppLoadingBar.vue`, #1689). It runs while
 * `useLoadActivity` counts anything out: a route change, a shared page's first
 * read, a read of TrueMain.
 *
 * Its own animation rather than Nuxt's `useLoadingIndicator`: that one is
 * finished by `page:loading:end`, which fires once the route has rendered —
 * now at once, while the page's data is still on its way.
 *
 * The progress is an estimate on the elapsed time, as Nuxt's: it closes on the
 * end without reaching it, so a cold read of tens of seconds still moves. A
 * 50 ms delay keeps an answer already cached from flashing the bar, and the
 * end is held for one more beat before the bar completes, so a navigation
 * handing over to the page's read reads as one load, not two.
 */
const { loading } = useLoadActivity()

const SHOW_AFTER_MS = 50
const SETTLE_MS = 120
const FILL_MS = 100
const FADE_MS = 300
/** Elapsed time at which the estimate reaches about 70 %. */
const ESTIMATE_MS = 2000

const progress = ref(0)
/** `finishing`: filled, fading out — the one phase with a CSS transition. */
const phase = ref<'idle' | 'running' | 'finishing'>('idle')

let frame = 0
let startedAt = 0
let showTimer: ReturnType<typeof setTimeout> | undefined
let settleTimer: ReturnType<typeof setTimeout> | undefined
let idleTimer: ReturnType<typeof setTimeout> | undefined

function step(now: number) {
  progress.value = (2 / Math.PI) * 100 * Math.atan(((now - startedAt) / ESTIMATE_MS) * 2)
  frame = requestAnimationFrame(step)
}

function show() {
  clearTimeout(idleTimer)
  phase.value = 'running'
  progress.value = 0
  startedAt = performance.now()
  cancelAnimationFrame(frame)
  frame = requestAnimationFrame(step)
}

function complete() {
  cancelAnimationFrame(frame)
  phase.value = 'finishing'
  progress.value = 100
  idleTimer = setTimeout(() => {
    phase.value = 'idle'
    progress.value = 0
  }, FILL_MS + FADE_MS)
}

watch(loading, (now) => {
  clearTimeout(showTimer)
  clearTimeout(settleTimer)
  if (now) {
    if (phase.value !== 'running') showTimer = setTimeout(show, SHOW_AFTER_MS)
  }
  else if (phase.value === 'running') {
    settleTimer = setTimeout(complete, SETTLE_MS)
  }
}, { immediate: true })

onBeforeUnmount(() => {
  cancelAnimationFrame(frame)
  clearTimeout(showTimer)
  clearTimeout(settleTimer)
  clearTimeout(idleTimer)
})
</script>

<template>
  <div
    aria-hidden="true"
    class="pointer-events-none fixed inset-x-0 top-0 z-50 h-0.5 origin-left bg-primary"
    :style="{
      opacity: phase === 'running' ? 1 : 0,
      transform: `scaleX(${progress / 100})`,
      transition: phase === 'finishing'
        ? `transform ${FILL_MS}ms linear, opacity ${FADE_MS}ms ease-out ${FILL_MS}ms`
        : 'none',
    }"
  />
</template>
