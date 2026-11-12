<script setup lang="ts">
import type { LeaderboardRowReadModel as LeaderboardRowResponse } from '#shared/contract/api'
import type { ChampionStaticListItem } from '~~/shared/types/static-data'
import { formatCompactCount } from '~~/shared/utils/counts'

// Variant F ("Dock") — the truemains teaser: the copy and the tracked total on
// one side, the shared leaderboard panel (`HomeTruemainsPanel`) on the other.
// Its card is a `.surface`, so its edge catches the cursor like every panel.
defineProps<{
  rows: LeaderboardRowResponse[]
  championsById: Map<number, ChampionStaticListItem>
  initialLoading: boolean
  patch: string | null
  tracked: number | null
}>()
</script>

<template>
  <section
    class="f-section f-split"
    aria-labelledby="f-mains-title"
  >
    <div
      v-reveal="{ effect: 'rise' }"
      class="f-split__copy"
    >
      <p class="eyebrow">
        Truemains
      </p>
      <h2
        id="f-mains-title"
        class="f-h2"
      >
        The players who truly main them.
      </h2>
      <p class="f-lede">
        A leaderboard of one-champion players: their rank, their main, and what they build on it.
      </p>
      <p
        v-if="tracked !== null"
        class="f-figure"
      >
        <FxCount
          :value="tracked"
          :format="formatCompactCount"
          :duration="1800"
          class="f-figure__num"
        />
        <span class="f-figure__caption">truemains tracked</span>
      </p>
      <NuxtLink
        to="/truemains"
        class="f-cta f-cta--ghost"
      >
        Open the leaderboard
        <kbd class="f-key">4</kbd>
      </NuxtLink>
    </div>

    <div
      v-reveal="{ effect: 'rise', delay: 90 }"
      class="f-split__panel"
    >
      <HomeTruemainsPanel
        :rows="rows"
        :champions-by-id="championsById"
        :initial-loading="initialLoading"
        :patch="patch"
      />
    </div>
  </section>
</template>
