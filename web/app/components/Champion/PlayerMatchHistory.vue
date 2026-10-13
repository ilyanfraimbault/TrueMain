<script setup lang="ts">
import type { ChampionPosition } from '#common/utils/positions'
import type {
  ChampionStaticListItem,
  RuneTreeResponse,
  StaticItemData,
  StaticSummonerSpellData,
} from '~~/shared/types/static-data'

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

function setPosition(next: ChampionPosition | null) {
  position.value = next
  page.value = 1
}
</script>

<template>
  <section class="flex min-w-0 flex-col gap-2">
    <div class="flex flex-wrap items-center justify-between gap-2">
      <h2 class="text-xs font-semibold uppercase tracking-wide text-muted">
        Recent {{ championName ?? '' }} games
      </h2>
      <RolePicker
        :position="position"
        @update:position="setPosition"
      />
    </div>

    <MatchHistoryList
      v-model:page="page"
      :matches="matches"
      :loading="isInitialLoading"
      :total="total"
      :page-size="pageSize"
      :champions="champions"
      :items="items"
      :summoner-spells="summonerSpells"
      :rune-tree="runeTree"
      :name-tag="nameTag"
      :not-found="notFound"
      :filtered="position !== null"
    />
  </section>
</template>
