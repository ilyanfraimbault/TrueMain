<script setup lang="ts">
import type { DesignHomeData } from '~/composables/useDesign'

// Variant F ("Dock") home: search first. The first screen is a field and the
// wall of every champion it filters; below it, the truemains and the desktop
// app. Navigation is the dock's job, so the page carries no menu of its own.
// Every figure comes from the page's payloads.
defineProps<DesignHomeData>()
</script>

<template>
  <div class="f-home">
    <DesignFLauncher
      :overview="overview"
      :overview-pending="overviewPending"
      :champions-by-id="championsById"
      :games-analyzed="gamesAnalyzed"
      :tracked-truemains="trackedTruemains"
    />

    <DesignFMains
      :rows="truemainRows"
      :champions-by-id="championsById"
      :initial-loading="truemainsInitialLoading"
      :patch="ddragonPatch"
      :tracked="trackedTruemains"
    />

    <DesignFDesktop />
  </div>
</template>
