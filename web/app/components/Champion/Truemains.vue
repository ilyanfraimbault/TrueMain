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

const { rows, isInitialLoading, error } = useTruemainsLeaderboard(1, {
  pageSize: TOP_N,
  championId: () => props.championId,
})

// Map keyed lookup for the row's top-3 — avoids a linear scan per icon.
const championsById = computed(() => {
  const map = new Map<number, ChampionStaticListItem>()
  for (const c of props.champions) map.set(c.championId, c)
  return map
})

const viewAllHref = computed(() => `/truemains?championId=${props.championId}`)
</script>

<template>
  <SectionCard
    :level="2"
    title="Truemains"
    subtitle="Top tracked players on this champion."
  >
    <div class="flex flex-col gap-2">
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
        Couldn't load truemains. Please try again.
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
