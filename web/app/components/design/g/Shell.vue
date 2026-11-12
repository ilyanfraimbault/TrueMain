<script setup lang="ts">
import { MAIN_CONTENT_ID } from '~/utils/route-focus'
import { startLightEngine } from './light-engine'

// Variant G ("Bento") — the whole frame: a centred floating pill for the
// navigation, the page, and a quiet footer. The light engine lives here, once
// for every route: it lights the edges of the tiles and panels near the cursor.
// A fine pointer only — touch has no cursor to follow.
const fx = useFx()
let stop: (() => void) | null = null

onMounted(() => {
  if (!fx.value.finePointer) return
  stop = startLightEngine()
})

onBeforeUnmount(() => stop?.())
</script>

<template>
  <div class="g-frame">
    <DesignGHeader />
    <main
      :id="MAIN_CONTENT_ID"
      tabindex="-1"
      class="g-main outline-none"
    >
      <slot />
    </main>
    <DesignGFooter />
  </div>
</template>
