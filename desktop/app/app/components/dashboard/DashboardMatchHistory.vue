<script setup lang="ts">
import type { RankHistoryEntry } from '#shared/types/rank-history'
import type { ChampionStaticListItem, RuneTreeResponse, StaticItemData, StaticSummonerSpellData } from '#shared/types/static-data'
import type { PlayerGame } from '~/types/record'
import { counted } from '~/utils/player-form'
import { toMatchSummary } from '~/utils/match-summary'
import { lpDeltaOf } from '~/utils/lp-history'
import { groupMatchesByDay } from '~/utils/match-history'
import { getQueueLabel } from '~/utils/queues'

/**
 * The player's match history, ten games a page with the site's pagination,
 * grouped by day on each page. Pages past what has been read ask the client for
 * older games (`loadOlder`); while it still has some, the pager offers one page
 * more than is loaded. Remakes count for nothing and the site never lists one,
 * so neither does this.
 */
const props = defineProps<{
  /** Every game read so far in the chosen queue, newest first. */
  games: PlayerGame[]
  /** Every game read so far, all queues — the neighbours an LP gain is measured between. */
  allGames: PlayerGame[]
  rankHistory: RankHistoryEntry[]
  queue: string
  pending: boolean
  hasOlder: boolean
  loadingOlder: boolean
  champions: ChampionStaticListItem[]
  items: Record<number, StaticItemData>
  summonerSpells: Record<number, StaticSummonerSpellData>
  runeTree: RuneTreeResponse | null
  nameTag: string
}>()

const emit = defineEmits<{ loadOlder: [] }>()

const PAGE_SIZE = 10
const page = ref(1)
watch(() => props.queue, () => (page.value = 1))

const listed = computed(() => counted(props.games))
const total = computed(() => listed.value.length + (props.hasOlder ? PAGE_SIZE : 0))
// The client ran out before the page it was asked for: land on the last one there is.
watch(total, (value) => {
  page.value = Math.min(page.value, Math.max(1, Math.ceil(value / PAGE_SIZE)))
})
const days = computed(() => groupMatchesByDay(
  listed.value
    .slice((page.value - 1) * PAGE_SIZE, page.value * PAGE_SIZE)
    .map(game => toMatchSummary(game, lpDeltaOf(game, props.allGames, props.rankHistory))),
))

// A page reaching past what is loaded reads older games until it is full, or
// the client has none left — a filtered queue can take more than one read.
const short = computed(() => listed.value.length < page.value * PAGE_SIZE)
watchEffect(() => {
  if (short.value && props.hasOlder && !props.loadingOlder) emit('loadOlder')
})

const section = ref<HTMLElement | null>(null)
watch(page, () => section.value?.scrollIntoView({ block: 'start', behavior: 'smooth' }))

const loadingPage = computed(() => props.pending || (listed.value.length > 0 && !props.runeTree) || (!days.value.length && props.loadingOlder))
</script>

<template>
  <section ref="section" class="flex min-w-0 scroll-mt-4 flex-col gap-1.5">
    <h2 class="text-xs font-semibold uppercase tracking-wide text-muted">Match history</h2>

    <template v-if="loadingPage">
      <USkeleton v-for="index in 5" :key="index" class="h-[54px] rounded-lg" />
    </template>
    <UEmpty
      v-else-if="!days.length"
      icon="i-lucide-swords"
      :title="queue === 'All' ? 'No games yet' : 'No games in this queue'"
      :description="queue === 'All' ? 'Your next game shows up here.' : 'None of your latest games were played in it.'"
      variant="naked"
      class="py-8"
    />
    <template v-for="day in days" v-else :key="day.key">
      <MatchDayHeading v-if="day.label" :label="day.label" class="pt-1.5" />
      <div class="flex flex-col gap-1.5">
        <DashboardMatchRow
          v-for="match in day.matches"
          :key="match.matchId"
          :match="match"
          :champions="champions"
          :items="items"
          :summoner-spells="summonerSpells"
          :rune-tree="runeTree!"
          :queue-label="getQueueLabel(match.queueId, match.gameMode)"
          :name-tag="nameTag"
        />
      </div>
    </template>

    <div v-if="total > PAGE_SIZE" class="flex justify-center pt-2">
      <UPagination
        v-model:page="page"
        :total="total"
        :items-per-page="PAGE_SIZE"
        :sibling-count="1"
        color="neutral"
        variant="ghost"
        active-color="primary"
        active-variant="soft"
        size="sm"
      />
    </div>
  </section>
</template>
