<script setup lang="ts">
import type { DesignHomeData } from '~/composables/useDesign'

// Variant D ("Cockpit") home: a dashboard, not a landing page. A short header
// with the jumps a regular makes, the KPI strip, the patch's meta as a
// master–detail panel, then the top truemains beside the desktop app. Every
// figure comes from the page's payloads; anything still loading is a skeleton.
const props = defineProps<DesignHomeData>()

const patch = computed(() => props.overview?.patchVersion ?? null)

const QUICK_LINKS = [
  { label: 'Tier list', icon: 'i-lucide-trending-up', to: '/champions/tierlist' },
  { label: 'Matchup', icon: 'i-lucide-wand-sparkles', to: '/matchup' },
  { label: 'Favorites', icon: 'i-lucide-star', to: '/truemains/favorites' },
]
</script>

<template>
  <div class="d-home">
    <header class="d-home__head">
      <div class="min-w-0">
        <p class="d-home__eyebrow">
          Overview
        </p>
        <h1 class="d-home__title">
          Real builds from <span class="d-sheen">real mains</span>.
        </h1>
        <p class="d-home__lede">
          Builds, runes and skill orders from the players who truly mastered your champion.
        </p>
      </div>
      <nav
        class="d-quick"
        aria-label="Quick links"
      >
        <NuxtLink
          v-for="link in QUICK_LINKS"
          :key="link.to"
          :to="link.to"
          class="d-quick__link"
        >
          <UIcon
            :name="link.icon"
            class="d-quick__icon"
          />
          {{ link.label }}
        </NuxtLink>
      </nav>
    </header>

    <div class="d-home__grid">
      <DesignDKpis
        :games-analyzed="gamesAnalyzed"
        :games-pending="overviewPending"
        :tracked-truemains="trackedTruemains"
        :truemains-loading="truemainsInitialLoading"
        :patch="patch"
      />

      <DesignDMeta
        :rows="overview?.topRows ?? []"
        :pending="tierlistPending"
        :champions-by-id="championsById"
        :patch="patch"
      />

      <div class="d-home__pair">
        <DesignDMains
          :rows="truemainRows"
          :champions-by-id="championsById"
          :initial-loading="truemainsInitialLoading"
          :patch="ddragonPatch"
        />
        <DesignDAppPromo />
      </div>
    </div>
  </div>
</template>
