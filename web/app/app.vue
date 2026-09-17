<script setup lang="ts">
import { MAIN_CONTENT_ID } from '~/utils/route-focus'
</script>

<template>
  <UApp :tooltip="{ delayDuration: 0 }">
    <NuxtRouteAnnouncer />
    <AppSkipLink />
    <AppHeader />

    <!-- `tabindex="-1"`: focusable by the skip link and after a navigation
         (#1616), never a tab stop. The focus ring is dropped because it would
         outline the whole page; the focus lands on the next Tab's target. -->
    <UMain
      :id="MAIN_CONTENT_ID"
      tabindex="-1"
      class="outline-none"
    >
      <NuxtPage />
    </UMain>

    <AppFooter />
  </UApp>
</template>
