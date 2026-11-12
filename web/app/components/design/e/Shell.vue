<script setup lang="ts">
import { MAIN_CONTENT_ID } from '~/utils/route-focus'

// Variant E ("Launcher") — the whole frame, TrueMain as a game client: an icon
// rail down the left edge (a tab bar along the bottom on a phone), the page in
// the column beside it, the footer at the column's foot. No top bar: the
// loading bar runs along the viewport's top edge (`--ui-header-height` in
// design-e.css).
//
// No cursor light (owner's call, round 3): the light engine stays off, so edges,
// the wordmark and the stage parallax no longer follow the pointer.
</script>

<template>
  <div class="e-frame">
    <DesignERail />

    <div class="e-column">
      <div class="e-mtop">
        <NuxtLink
          to="/"
          class="e-mtop__logo"
          aria-label="TrueMain home"
        >
          <AppLogo class="text-[17px]" />
        </NuxtLink>
      </div>

      <main
        :id="MAIN_CONTENT_ID"
        tabindex="-1"
        class="e-main outline-none"
      >
        <slot />
      </main>

      <DesignEFooter />
    </div>

    <DesignETabBar />
  </div>
</template>
