<script setup lang="ts">
import { startSpotlightEngine } from './spotlight-engine'

// Variant B's room: the dark wall every page hangs on. Rendered once, from the
// header (mounted on every route), as fixed layers:
//
// - the wall (`z-index: -1`, behind all content): a faint overhead ambient, the
//   torch glow that trails the cursor and the dot texture it reveals;
// - the grain (above content, under the header): a static film grain so the
//   flat blacks have a tooth.
//
// The cursor light only exists for a fine pointer (touch has no cursor to follow). `lite` drops the canvas
// texture; reduced motion also drops the trailing inertia.
const fx = useFx()
const glowEl = ref<HTMLElement | null>(null)
const canvasEl = ref<HTMLCanvasElement | null>(null)
const lit = ref(false)
const enabled = ref(false)
const withTexture = ref(false)
let stop: (() => void) | null = null

onMounted(async () => {
  const { finePointer, reducedMotion, level } = fx.value
  if (!finePointer) return
  enabled.value = true
  withTexture.value = level === 'full' && !reducedMotion
  await nextTick()
  if (!glowEl.value) return
  stop = startSpotlightEngine({
    glow: glowEl.value,
    canvas: withTexture.value ? canvasEl.value : null,
    smooth: !reducedMotion,
    onActiveChange: (active) => {
      lit.value = active
    },
  })
})

onBeforeUnmount(() => stop?.())
</script>

<template>
  <div
    aria-hidden="true"
    class="b-wall"
    :class="{ 'is-lit': lit }"
  >
    <div class="b-wall__ambient" />
    <template v-if="enabled">
      <div
        ref="glowEl"
        class="b-wall__glow"
      />
      <canvas
        v-if="withTexture"
        ref="canvasEl"
        class="b-wall__dots"
      />
    </template>
  </div>
  <div
    aria-hidden="true"
    class="b-grain"
  />
</template>
