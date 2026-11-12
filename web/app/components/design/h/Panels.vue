<script setup lang="ts">
import type { ChampionOverviewRowReadModel as ChampionOverviewRow } from '#shared/contract/api'
import type { LeaderboardRowReadModel as LeaderboardRowResponse } from '#shared/contract/api'
import type { ChampionStaticListItem } from '~~/shared/types/static-data'

// Variant H home, second screen: the patch at a glance — the ring's champions
// as a comparable table, beside the players who main them. The site's own
// panels (`HomeTierlistPanel`, `HomeTruemainsPanel`), set in glass-rimmed
// `.surface` panes whose edges catch the cursor's light (design-h.css).
defineProps<{
  topRows: ChampionOverviewRow[]
  tierlistPending: boolean
  championsById: Map<number, ChampionStaticListItem>
  truemainRows: LeaderboardRowResponse[]
  truemainsInitialLoading: boolean
  patch: string | null
}>()
</script>

<template>
  <section
    class="h-section"
    aria-labelledby="h-patch-title"
  >
    <header class="h-head">
      <p class="h-head__eyebrow">
        This patch
      </p>
      <h2
        id="h-patch-title"
        class="h-head__title h-lit h-lit--scroll"
        data-h-spot
      >
        The most played and banned champions, and the players who main them.
      </h2>
    </header>

    <div
      v-reveal.stagger="{ step: 110, effect: 'rise' }"
      class="h-panels"
    >
      <HomeTierlistPanelSkeleton v-if="tierlistPending" />
      <HomeTierlistPanel
        v-else
        :top-rows="topRows"
        :champions-by-id="championsById"
      />
      <HomeTruemainsPanel
        :rows="truemainRows"
        :champions-by-id="championsById"
        :initial-loading="truemainsInitialLoading"
        :patch="patch"
      />
    </div>
  </section>
</template>
