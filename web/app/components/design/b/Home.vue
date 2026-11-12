<script setup lang="ts">
import type { DesignHomeData } from '~/composables/useDesign'

// Variant B ("Spotlight") home: light is the interaction. The eclipse lights the
// headline, a torch follows the cursor across the wall, panels light their edges
// as it passes, a lamp swings over the desktop app, and a stage light switches
// on for the closing call. Every figure comes from the page's payloads.
const props = defineProps<DesignHomeData>()

const patch = computed(() => props.overview?.patchVersion ?? null)
</script>

<template>
  <div class="b-home">
    <DesignBHero
      :patch="patch"
      :overview-pending="overviewPending"
      :games-analyzed="gamesAnalyzed"
      :tracked-truemains="trackedTruemains"
    />

    <DesignBChampionShowcase
      :overview="overview"
      :pending="tierlistPending"
      :champions-by-id="championsById"
    />

    <DesignBDesktopLamp />

    <DesignBTruemainsSection
      :rows="truemainRows"
      :champions-by-id="championsById"
      :initial-loading="truemainsInitialLoading"
      :patch="ddragonPatch"
      :tracked="trackedTruemains"
    />

    <DesignBFinalCta />
  </div>
</template>
