<script setup lang="ts">
import { MAIN_CONTENT_ID } from '~/utils/route-focus'
import { DOCK_SEARCH_KEY } from './search-context'
import { startEdgeLight } from './edge-light'

// Variant F ("Dock") — the whole frame of every page. No header: a small mark
// top-left, the page, a one-line footer, and the navigation in a dock floating
// at the bottom of the viewport. The dock sinks a little while the reader
// scrolls down and comes back on the way up, at the end of the page, or as soon
// as the cursor nears the bottom edge.
const route = useRoute()
const dock = ref<{ openPalette: () => void } | null>(null)
const receded = ref(false)

/**
 * Open the dock's palette, optionally with a term already typed: the home's
 * champion field passes what it could not match, so a player's name typed
 * there becomes a player search without retyping it.
 */
function openSearch(term = '') {
  dock.value?.openPalette()
  const value = term.trim()
  if (!value) return
  const started = performance.now()
  const fill = () => {
    const input = document.querySelector<HTMLInputElement>('[role="dialog"][data-state="open"] input')
    if (input) {
      input.value = value
      input.dispatchEvent(new Event('input', { bubbles: true }))
      return
    }
    // The palette's chunk loads on its first open: wait for it, briefly.
    if (performance.now() - started < 2500) requestAnimationFrame(fill)
  }
  requestAnimationFrame(fill)
}

provide(DOCK_SEARCH_KEY, { open: openSearch })

watch(() => route.path, () => {
  receded.value = false
})

let lastY = 0
let scrollFrame = 0
const EDGE = 120

function onScroll() {
  if (scrollFrame) return
  scrollFrame = requestAnimationFrame(() => {
    scrollFrame = 0
    const y = window.scrollY
    const dy = y - lastY
    if (Math.abs(dy) < 6) return
    lastY = y
    const atEnd = y + window.innerHeight >= document.documentElement.scrollHeight - 48
    receded.value = dy > 0 && y > 160 && !atEnd
  })
}

function onPointerMove(event: PointerEvent) {
  if (receded.value && event.pointerType !== 'touch' && event.clientY > window.innerHeight - EDGE) receded.value = false
}

let stopEdgeLight: (() => void) | null = null

onMounted(() => {
  lastY = window.scrollY
  window.addEventListener('scroll', onScroll, { passive: true })
  window.addEventListener('pointermove', onPointerMove, { passive: true })
  const finePointer = window.matchMedia('(hover: hover) and (pointer: fine)').matches
  const reduced = window.matchMedia('(prefers-reduced-motion: reduce)').matches
  if (finePointer && !reduced) stopEdgeLight = startEdgeLight()
})

onBeforeUnmount(() => {
  window.removeEventListener('scroll', onScroll)
  window.removeEventListener('pointermove', onPointerMove)
  cancelAnimationFrame(scrollFrame)
  stopEdgeLight?.()
})
</script>

<template>
  <div class="f-shell">
    <div class="f-top">
      <NuxtLink
        to="/"
        class="f-top__logo"
        aria-label="TrueMain — home"
      >
        <AppLogo class="text-[17px]" />
      </NuxtLink>
      <button
        type="button"
        class="f-top__search"
        aria-label="Search a champion or player"
        @click="openSearch()"
      >
        <UIcon
          name="i-lucide-search"
          class="size-[18px]"
        />
      </button>
    </div>

    <main
      :id="MAIN_CONTENT_ID"
      tabindex="-1"
      class="f-main outline-none"
    >
      <slot />
    </main>

    <DesignFFooter />
    <DesignFDock
      ref="dock"
      :receded="receded"
    />
  </div>
</template>
