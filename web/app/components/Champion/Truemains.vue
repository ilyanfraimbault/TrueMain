<script setup lang="ts">
import type { ChampionStaticListItem, RuneTreeResponse, StaticItemData } from '~~/shared/types/static-data'
import { describeFetchError } from '~/utils/errors'

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

// `server: false` (#1231): the champion page renders this card behind
// `hydrate-on-visible`, which defers *hydration*, not server rendering — so the
// composable's SSR default was firing `/api/truemains?championId=…` on every
// SSR of every champion page, uncached by Nitro, for a card below the fold.
// That page's SSR round-trip budget is deliberately spent on the build summary
// alone (#1123, #926); the skeleton below is the server-rendered state here.
const { rows, isInitialLoading, error } = useTruemainsLeaderboard(1, {
  pageSize: TOP_N,
  championId: () => props.championId,
  server: false,
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
    subtitle="Top tracked players on this champion."
    :ui="{ header: 'p-2 sm:px-2.5 sm:py-2', body: 'p-1.5 sm:p-2' }"
  >
    <div class="flex flex-col gap-1">
      <template v-if="isInitialLoading">
        <LeaderboardRowSkeleton
          v-for="i in 5"
          :key="`tm-skel-${i}`"
        />
      </template>

      <p
        v-else-if="error"
        class="py-6 text-center text-sm text-muted"
      >
        {{ describeFetchError(error) }}
      </p>

      <p
        v-else-if="rows.length === 0"
        class="py-6 text-center text-sm text-muted"
      >
        No tracked truemains on this champion yet.
      </p>

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
