<script setup lang="ts">
import type { LeaderboardRowReadModel as LeaderboardRowResponse } from '#shared/contract/api'
import type { ChampionStaticListItem } from '~~/shared/types/static-data'
import { formatCompactCount } from '~~/shared/utils/counts'

// The truemains, as a launcher's friends/featured shelf: the top of the
// leaderboard as player cards, the first one larger. The cards dim around the
// one under the cursor.
defineProps<{
  rows: LeaderboardRowResponse[]
  championsById: Map<number, ChampionStaticListItem>
  loading: boolean
  patch: string | null
  tracked: number | null
}>()
</script>

<template>
  <section
    class="e-section"
    aria-labelledby="e-players-title"
  >
    <div class="e-section__head">
      <div>
        <p class="e-eyebrow">
          Truemains
        </p>
        <h2
          id="e-players-title"
          class="e-title"
        >
          The players who truly main them.
        </h2>
        <p class="e-lede">
          One-champion players at the top of the ladder: their rank, their main, and how much of their play it takes.
        </p>
      </div>
      <div class="e-section__aside">
        <p
          v-if="tracked !== null"
          class="e-figure"
        >
          <span class="e-figure__value"><FxCount
            :value="tracked"
            :format="formatCompactCount"
            :duration="1600"
          /></span>
          <span class="e-figure__label">truemains tracked</span>
        </p>
        <UButton
          to="/truemains"
          color="neutral"
          variant="ghost"
          trailing-icon="i-lucide-arrow-right"
          label="Leaderboard"
          class="e-ghost"
        />
      </div>
    </div>

    <div
      v-if="loading"
      class="e-players"
      aria-hidden="true"
    >
      <span
        v-for="i in 7"
        :key="i"
        class="e-player e-player--skeleton"
        :class="{ 'e-player--featured': i === 1 }"
      />
    </div>

    <ul
      v-else-if="rows.length"
      class="e-players"
    >
      <li
        v-for="(row, i) in rows"
        :key="`${row.identity.gameName}-${row.identity.tagLine}`"
        :class="{ 'e-players__featured': i === 0 }"
      >
        <DesignEPlayerCard
          :row="row"
          :champions-by-id="championsById"
          :patch="patch"
          :featured="i === 0"
        />
      </li>
    </ul>

    <p
      v-else
      class="e-empty"
    >
      No ranked truemains yet.
    </p>
  </section>
</template>
