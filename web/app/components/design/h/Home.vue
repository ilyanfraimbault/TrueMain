<script setup lang="ts">
import type { DesignHomeData } from '~/composables/useDesign'

// Variant H ("Glass Arena") home: the patch's champions on a ring in the
// eclipse first, then the patch at a glance with the players who main it, then
// the desktop app under its lamp. Every figure comes from the page's payloads.
const props = defineProps<DesignHomeData>()

const topRows = computed(() => props.overview?.topRows ?? [])
</script>

<template>
  <div class="h-home">
    <DesignHArena
      :overview="overview"
      :overview-pending="overviewPending"
      :tierlist-pending="tierlistPending"
      :champions-by-id="championsById"
      :games-analyzed="gamesAnalyzed"
      :tracked-truemains="trackedTruemains"
    />

    <DesignHPanels
      :top-rows="topRows"
      :tierlist-pending="tierlistPending"
      :champions-by-id="championsById"
      :truemain-rows="truemainRows"
      :truemains-initial-loading="truemainsInitialLoading"
      :patch="ddragonPatch"
    />

    <DesignHDesktop />
  </div>
</template>
