<script setup lang="ts">
import type { ChampionPosition } from '~/utils/positions'
import type {
  ChampionStaticListItem,
  RuneTreeResponse,
  StaticItemData,
  StaticSummonerSpellData,
} from '~~/shared/types/static-data'
import { groupMatchesByDay } from '~/utils/match-history'

// One player's recent games on ONE champion, as rendered under the build on
// the player-scoped champion page. It owns its own fetch and its own lane
// filter on purpose: the champion is fixed by the page, but the lane picker
// here is independent of the build's position filter, so you can browse games
// on any lane without re-scoping the build above.
const props = defineProps<{
  nameTag: string
  championId: number
  /** Champion display name for the heading; null while the statics load. */
  championName: string | null
  /**
   * The static bundle the rows need. Passed in rather than fetched here so
   * this list shares the page's already-resolved (and patch-keyed) fetches
   * instead of opening its own.
   */
  champions: ChampionStaticListItem[] | null | undefined
  items: Record<number, StaticItemData> | null | undefined
  summonerSpells: Record<number, StaticSummonerSpellData> | null | undefined
  runeTree: RuneTreeResponse | null | undefined
}>()

const page = ref(1)
const position = ref<ChampionPosition | null>(null)

const {
  matches,
  total,
  pageSize,
  isInitialLoading,
  notFound,
} = useTruemainMatches(
  () => props.nameTag,
  page,
  {
    championId: () => props.championId,
    position,
  },
)

function setPage(next: number) {
  page.value = Math.max(1, Math.floor(next))
}

function setPosition(next: ChampionPosition | null) {
  position.value = next
  page.value = 1
}

// Same dated day-runs as the profile history.
const matchDays = computed(() => groupMatchesByDay(matches.value))

// Nullable on purpose: each of the four is a separate static fetch, and the
// rows can only be drawn once all four have landed. Coercing them to `{}` at
// the call site would make this check pass over empty maps and render rows
// with no items on them.
const staticBundleReady = computed(() =>
  Boolean(props.champions && props.items && props.summonerSpells && props.runeTree),
)
</script>

<template>
  <section class="flex min-w-0 flex-col gap-3">
    <div class="flex flex-wrap items-center justify-between gap-2">
      <h2 class="text-xs font-semibold uppercase tracking-wide text-muted">
        Recent {{ championName ?? '' }} games
      </h2>
      <RolePicker
        :position="position"
        @update:position="setPosition"
      />
    </div>

    <!--
      Same ordering as the profile page: the empty / not-found state needs no
      static data, so it must not sit behind staticBundleReady — a failing
      static fetch would pin the skeletons forever.
    -->
    <template v-if="isInitialLoading">
      <MatchRowSkeleton v-for="i in 5" :key="`match-skel-${i}`" />
    </template>
    <template v-else-if="notFound || matches.length === 0">
      <MatchHistoryEmpty :not-found="notFound" :filtered="position !== null" />
    </template>
    <template v-else-if="!staticBundleReady">
      <MatchRowSkeleton v-for="i in 5" :key="`match-skel-${i}`" />
    </template>
    <template v-else>
      <!-- Same day grouping as the profile history, so the two lists read
           identically. -->
      <template v-for="day in matchDays" :key="day.key">
        <MatchDayHeading v-if="day.label" :label="day.label" />
        <LazyMatchRow
          v-for="match in day.matches"
          :key="match.matchId"
          hydrate-on-visible
          :match="match"
          :champions="champions!"
          :items="items!"
          :summoner-spells="summonerSpells!"
          :rune-tree="runeTree!"
          :name-tag="nameTag"
        />
      </template>
      <div
        v-if="total > pageSize"
        class="flex justify-center pt-2"
      >
        <UPagination
          :page="page"
          :total="total"
          :items-per-page="pageSize"
          :sibling-count="1"
          color="neutral"
          variant="ghost"
          active-color="primary"
          active-variant="soft"
          @update:page="setPage"
        />
      </div>
    </template>
  </section>
</template>
