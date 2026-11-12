<script setup lang="ts">
import { MAIN_CONTENT_ID } from '~/utils/route-focus'
import { startLightEngine } from './light-engine'

// Variant H ("Glass Arena") — the frame of every page. The navigation leaves
// the top of the page for a floating liquid-glass tab bar at the bottom centre
// (`TabBar`); the top keeps only the logo, transparent, and scrolls away with
// the page. Panel edges catch the cursor's light site-wide (`light-engine.ts`),
// a fine pointer only — touch has no cursor to follow.
const fx = useFx()
let stopLight: (() => void) | null = null

onMounted(() => {
  if (fx.value.finePointer) stopLight = startLightEngine()
})
onBeforeUnmount(() => stopLight?.())
</script>

<template>
  <div class="h-frame">
    <header class="h-top">
      <NuxtLink
        to="/"
        class="h-top__logo"
        aria-label="TrueMain home"
      >
        <AppLogo class="text-lg" />
      </NuxtLink>
      <DesignHTabBar />
    </header>

    <main
      :id="MAIN_CONTENT_ID"
      tabindex="-1"
      class="h-main outline-none"
    >
      <slot />
    </main>

    <DesignHFooter />
  </div>
</template>
