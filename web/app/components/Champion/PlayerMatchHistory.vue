<script setup lang="ts">
import type { ChampionPosition } from '#common/utils/positions'
import type { MatchSummaryResponse } from '~~/shared/types/matches'
import type {
  ChampionStaticListItem,
  RuneTreeResponse,
  StaticItemData,
  StaticSummonerSpellData,
} from '~~/shared/types/static-data'

// One player's recent games on ONE champion, as rendered under the build on
// the player-scoped champion page. The page owns the fetch, since the banner's
// form tiles read the same games; this list owns only its heading and its lane
// filter, which is independent of the build's position filter, so you can
// browse games on any lane without re-scoping the build above.
defineProps<{
  nameTag: string
  /** Champion display name for the heading; null while the statics load. */
  championName: string | null
  matches: MatchSummaryResponse[]
  total: number
  pageSize: number
  loading: boolean
  notFound: boolean
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

const page = defineModel<number>('page', { required: true })
const position = defineModel<ChampionPosition | null>('position', { required: true })

// A new lane starts back on the first page: the old one may be out of range.
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
      :loading="loading"
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
