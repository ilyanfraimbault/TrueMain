<script setup lang="ts">
import type { ChampionStaticListItem, RuneTreeResponse, StaticItemData } from '~~/shared/types/static-data'

// Top truemains on this champion — the same rows as /truemains filtered by
// championId, capped at the first page with a link through to the full
// filtered leaderboard. LeaderboardRow is an @container, so the rows render
// their compact shape in the sidebar without any props.
const props = defineProps<{
  championId: number
  champions: ChampionStaticListItem[]
  /** Static rune tree + item map, to draw each row's main-champion build. */
  runeTree: RuneTreeResponse | null
  itemsMap: Record<number, StaticItemData>
  /** Full ddragon version for profile-icon URLs (not the short patch). */
  patch: string | null
}>()

const TOP_N = 10

// Server-rendered (#2359, reversing #1231's `server: false`): the champion's top
// truemains are what "{champion} otp" / "one trick" queries look for, and a
// skeleton gave a crawler none of them. The read costs the champion page's SSR
// one leaderboard round-trip, which #1231 had saved; it runs alongside the build
// summary rather than after it. `hydrate-on-visible` only defers hydration, and
// the rows ride the payload, so the deferred hydration finds the same markup.
const { rows, isInitialLoading, error } = useTruemainsLeaderboard(1, {
  pageSize: TOP_N,
  championId: () => props.championId,
})

// Deepest ordinal shown, so every row sizes its rank slot identically.
const deepestRank = computed(() => rows.value.reduce((max, row) => Math.max(max, row.rank), 0))

// Map keyed lookup for the row's top-3 — avoids a linear scan per icon.
const championsById = useChampionsById(() => props.champions)

const viewAllHref = computed(() => `/truemains?championId=${props.championId}`)
</script>

<template>
  <!-- Tighter card padding than the app default (`p-3 sm:p-4`): this card sits
       in the champion page's narrow sidebar, where the rows are already fighting
       for the width the Riot ID needs. Header trimmed with the body so the title
       stays flush with the rows. -->
  <SectionCard
    :level="2"
    title="Truemains"
    :ui="{ header: 'p-2 sm:px-2.5 sm:py-2', body: 'p-1.5 sm:p-2' }"
  >
    <div class="flex flex-col gap-1">
      <template v-if="isInitialLoading">
        <LeaderboardRowSkeleton
          v-for="i in 5"
          :key="`tm-skel-${i}`"
        />
      </template>

      <!-- An alert, not the muted line the empty state below uses: rendered in
           the same grey, "this failed" and "there is nothing here" were the same
           panel to a reader (#1661). -->
      <FetchErrorAlert
        v-else-if="error"
        :error="error"
        title="Failed to load the truemains"
        class="my-2"
      />

      <UEmpty
        v-else-if="rows.length === 0"
        size="sm"
        icon="i-lucide-trophy"
        description="No tracked truemains on this champion yet."
      />

      <template v-else>
        <LeaderboardRow
          v-for="row in rows"
          :key="row.rank"
          :row="row"
          :champions-by-id="championsById"
          :rune-tree="runeTree"
          :items-map="itemsMap"
          :patch="patch"
          :max-rank="deepestRank"
        />
        <div class="flex justify-end pt-1">
          <UButton
            :to="viewAllHref"
            color="neutral"
            variant="ghost"
            size="sm"
            trailing-icon="i-lucide-arrow-right"
            label="View all truemains"
          />
        </div>
      </template>
    </div>
  </SectionCard>
</template>
