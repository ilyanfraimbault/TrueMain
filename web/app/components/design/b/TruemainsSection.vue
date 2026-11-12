<script setup lang="ts">
import type { LeaderboardRowReadModel as LeaderboardRowResponse } from '#shared/contract/api'
import type { ChampionStaticListItem } from '~~/shared/types/static-data'
import { formatCompactCount } from '~~/shared/utils/counts'

// The truemains teaser: the copy and the tracked total on one side, the shared
// leaderboard panel (`HomeTruemainsPanel`, unchanged) on the other — its card is
// a `.surface`, so its edge catches the cursor like every panel on the site.
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
    class="b-section b-mains"
    aria-labelledby="b-mains-title"
  >
    <div class="b-mains__grid">
      <div class="b-mains__copy">
        <DesignBSectionHead
          id="b-mains-title"
          eyebrow="Truemains"
        >
          The players who truly main them.
          <template #lede>
            A leaderboard of one-champion players: their rank, their main, and what they build on it.
          </template>
        </DesignBSectionHead>

        <div
          v-if="tracked !== null"
          class="b-mains__figure"
        >
          <span class="b-mains__number"><FxCount
            :value="tracked"
            :format="formatCompactCount"
            :duration="2200"
          /></span>
          <span class="b-mains__caption">truemains tracked</span>
        </div>

        <UButton
          v-magnetic="0.3"
          to="/truemains"
          color="neutral"
          variant="subtle"
          size="lg"
          trailing-icon="i-lucide-arrow-right"
          label="Open the leaderboard"
          class="b-cta b-cta--ghost mt-8"
        />
      </div>

      <div
        v-reveal="{ effect: 'blur' }"
        class="b-mains__panel"
      >
        <HomeTruemainsPanel
          :rows="rows"
          :champions-by-id="championsById"
          :initial-loading="initialLoading"
          :patch="patch"
        />
      </div>
    </div>
  </section>
</template>
