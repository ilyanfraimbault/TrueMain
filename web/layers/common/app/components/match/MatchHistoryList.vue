<!--
  The one day-grouped match list — the profile's history (site and app), the
  app's dashboard and the player-scoped champion page: `MatchRowSkeleton`s while
  the page or the statics load, `MatchHistoryEmpty` when there is nothing to show,
  else the rows under a `MatchDayHeading` per local day, and the pager. Each host
  keeps its own heading and filters around it. A host control on each row (the
  app's "Watch") goes in the `actions` slot, scoped by the row's match.
-->
<script setup lang="ts">
import type { MatchSummaryResponse } from '#shared/types/matches'
import type {
  ChampionStaticListItem,
  RuneTreeResponse,
  StaticItemData,
  StaticSummonerSpellData,
} from '#shared/types/static-data'
import { groupMatchesByDay } from '#common/utils/match-history'

const SKELETON_ROWS = 6

const props = defineProps<{
  /** The page on screen, newest first — the list cuts it into days, it never re-sorts. */
  matches: MatchSummaryResponse[]
  /** The page is being read; the statics are waited on here. */
  loading: boolean
  /** Every match there is, for the pager. */
  total: number
  pageSize: number
  /** Nullable: each is its own static read, and a row is drawn only once all four landed. */
  champions: ChampionStaticListItem[] | null | undefined
  items: Record<number, StaticItemData> | null | undefined
  summonerSpells: Record<number, StaticSummonerSpellData> | null | undefined
  runeTree: RuneTreeResponse | null | undefined
  /** The player's slug; it makes each row an accordion over the game's detail. */
  nameTag?: string | null
  notFound?: boolean
  filtered?: boolean
  own?: boolean
}>()

const page = defineModel<number>('page', { required: true })

defineSlots<{ actions?: (props: { match: MatchSummaryResponse }) => unknown }>()

// The empty state needs no static data, so it never waits on it: a failing
// static read (CDragon lagging a new patch) would otherwise pin the skeletons
// over a perfectly valid empty result.
const empty = computed(() => !props.loading && (props.notFound || props.matches.length === 0))

// Checked non-empty rather than non-null: a host that coerces a pending read to
// `{}` must not get rows drawn with no items on them.
const statics = computed(() => {
  const { champions, items, summonerSpells, runeTree } = props
  const ready = !!champions?.length
    && !!items && Object.keys(items).length > 0
    && !!summonerSpells && Object.keys(summonerSpells).length > 0
    && !!runeTree?.styles.length
  return ready ? { champions, items, summonerSpells, runeTree } : null
})

// One lookup for the whole page rather than one per row (#2129).
const championById = computed(() => new Map((props.champions ?? []).map(c => [c.championId, c])))

const days = computed(() => groupMatchesByDay(props.matches))
</script>

<template>
  <div class="flex min-w-0 flex-col gap-2">
    <MatchHistoryEmpty v-if="empty" :not-found="notFound" :filtered="filtered" :own="own" />
    <template v-else-if="loading || !statics">
      <MatchRowSkeleton v-for="index in SKELETON_ROWS" :key="index" />
    </template>
    <template v-for="day in days" v-else :key="day.key">
      <MatchDayHeading v-if="day.label" :label="day.label" />
      <MatchRow
        v-for="match in day.matches"
        :key="match.matchId"
        :match="match"
        :champions="statics.champions"
        :champion-by-id="championById"
        :items="statics.items"
        :summoner-spells="statics.summonerSpells"
        :rune-tree="statics.runeTree"
        :name-tag="nameTag"
      >
        <template v-if="$slots.actions" #actions>
          <slot name="actions" :match="match" />
        </template>
      </MatchRow>
    </template>

    <div v-if="!empty && total > pageSize" class="flex justify-center pt-2">
      <UPagination
        v-model:page="page"
        :total="total"
        :items-per-page="pageSize"
        :sibling-count="1"
        color="neutral"
        variant="ghost"
        active-color="primary"
        active-variant="soft"
      />
    </div>
  </div>
</template>
