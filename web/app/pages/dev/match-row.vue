<script setup lang="ts">
import type { MatchSummaryResponse } from '~~/shared/types/matches'
import type {
  ChampionStaticListItem,
  RuneTreeResponse,
  StaticItemData,
  StaticSummonerSpellData,
} from '~~/shared/types/static-data'

definePageMeta({ layout: 'default' })

useSeoMeta({
  title: 'MatchRow playground · TrueMain',
  description: 'Isolated visual review of the match history row component.',
})

// ─── Static lookups ────────────────────────────────────────────────────────
// Same fetches the production profile page will use — keeps the playground
// honest about the resolved icon URLs and lets us spot wiring problems here
// before they hit the live page.

const { data: champions } = useLazyAsyncData<ChampionStaticListItem[]>(
  'dev-match-row-champions',
  () => $fetch<ChampionStaticListItem[]>('/api/static/champions'),
  { default: () => [], server: false },
)

const { data: items } = useLazyAsyncData<Record<number, StaticItemData>>(
  'dev-match-row-items',
  () => $fetch<Record<number, StaticItemData>>('/api/static/items', { query: {} }),
  { default: () => ({}), server: false },
)

const { data: summonerSpells } = useLazyAsyncData<Record<number, StaticSummonerSpellData>>(
  'dev-match-row-summoner-spells',
  () => $fetch<Record<number, StaticSummonerSpellData>>('/api/static/summoner-spells', { query: {} }),
  { default: () => ({}), server: false },
)

const { data: runeTree } = useLazyAsyncData<RuneTreeResponse>(
  'dev-match-row-rune-tree',
  () => $fetch<RuneTreeResponse>('/api/static/rune-tree', { query: {} }),
  { default: () => ({ styles: [], perks: {}, perkStyles: {}, shardSlots: [] }), server: false },
)

// ─── Mock matches ──────────────────────────────────────────────────────────
// Three rows covering the visual states we need to support today:
//   1. Win + MVP (high-KDA aggressive carry game)
//   2. Loss + ACE (defeat but best on losing side)
//   3. Loss, no badge (mid-of-the-pack performance)

const now = new Date()
function isoMinutesAgo(minutes: number): string {
  return new Date(now.getTime() - minutes * 60 * 1000).toISOString()
}

function makeMockParticipants(): MatchSummaryResponse['participants'] {
  // Champion ids picked to be widely available across patches so the icons
  // resolve cleanly (Yasuo, Lux, Garen, Ahri, Thresh on each side).
  return [
    { championId: 157, teamId: 100, gameName: 'BlueTop', tagLine: 'EUW' },
    { championId: 64, teamId: 100, gameName: 'BlueJng', tagLine: 'EUW' },
    { championId: 99, teamId: 100, gameName: 'BlueMid', tagLine: 'EUW' },
    { championId: 222, teamId: 100, gameName: 'BlueBot', tagLine: 'EUW' },
    { championId: 412, teamId: 100, gameName: 'BlueSup', tagLine: 'EUW' },
    { championId: 86, teamId: 200, gameName: 'RedTop', tagLine: 'EUW' },
    { championId: 121, teamId: 200, gameName: 'RedJng', tagLine: 'EUW' },
    { championId: 103, teamId: 200, gameName: 'RedMid', tagLine: 'EUW' },
    { championId: 51, teamId: 200, gameName: 'RedBot', tagLine: 'EUW' },
    { championId: 117, teamId: 200, gameName: 'RedSup', tagLine: 'EUW' },
  ]
}

const mockMatches = computed<MatchSummaryResponse[]>(() => [
  {
    matchId: 'EUW1_MOCK_1',
    queueId: 420,
    gameMode: 'CLASSIC',
    gameStartTimeUtc: isoMinutesAgo(60 * 8),
    gameDurationSeconds: 1789,
    self: {
      championId: 157,
      championLevel: 18,
      summoner1Id: 4,
      summoner2Id: 12,
      primaryStyleId: 8000,
      subStyleId: 8200,
      keystoneId: 8005,
      kills: 13,
      deaths: 3,
      assists: 5,
      cs: 215,
      killParticipation: 0.67,
      items: [3031, 3046, 3036, 3072, 3009, 3026],
      trinketItemId: 3340,
      teamId: 100,
      win: true,
      lpDelta: 23,
      isMvp: true,
      isAce: false,
    },
    participants: makeMockParticipants(),
  },
  {
    matchId: 'EUW1_MOCK_2',
    queueId: 420,
    gameMode: 'CLASSIC',
    gameStartTimeUtc: isoMinutesAgo(60 * 30),
    gameDurationSeconds: 2189,
    self: {
      championId: 222,
      championLevel: 17,
      summoner1Id: 4,
      summoner2Id: 7,
      primaryStyleId: 8000,
      subStyleId: 8400,
      keystoneId: 8008,
      kills: 13,
      deaths: 3,
      assists: 5,
      cs: 263,
      killParticipation: 0.67,
      items: [6672, 3094, 3006, 3031, 3036, 0],
      trinketItemId: 3340,
      teamId: 200,
      win: false,
      lpDelta: -30,
      isMvp: false,
      isAce: true,
    },
    participants: makeMockParticipants(),
  },
  {
    matchId: 'EUW1_MOCK_3',
    queueId: 440,
    gameMode: 'CLASSIC',
    gameStartTimeUtc: isoMinutesAgo(60 * 26),
    gameDurationSeconds: 1620,
    self: {
      championId: 99,
      championLevel: 16,
      summoner1Id: 4,
      summoner2Id: 14,
      primaryStyleId: 8200,
      subStyleId: 8100,
      keystoneId: 8214,
      kills: 4,
      deaths: 6,
      assists: 8,
      cs: 188,
      killParticipation: 0.42,
      items: [6655, 3020, 3157, 3165, 0, 0],
      trinketItemId: 3340,
      teamId: 100,
      win: false,
      lpDelta: -18,
      isMvp: false,
      isAce: false,
    },
    participants: makeMockParticipants(),
  },
])

// ─── Live data toggle ──────────────────────────────────────────────────────
// Let the reviewer paste a real nameTag to see the row against live data.

const nameTagInput = ref('')
const liveNameTag = ref('')
const {
  matches: liveMatches,
  isLoading: liveIsLoading,
  isInitialLoading: liveIsInitialLoading,
  notFound: liveNotFound,
  hasMore: liveHasMore,
  loadMore: liveLoadMore,
} = useTruemainMatches(liveNameTag, { pageSize: 5 })

function applyLive() {
  liveNameTag.value = nameTagInput.value.trim()
}

const staticBundleReady = computed(() =>
  champions.value.length > 0
  && Object.keys(items.value).length > 0
  && Object.keys(summonerSpells.value).length > 0
  && (runeTree.value?.styles.length ?? 0) > 0,
)
</script>

<template>
  <div class="mx-auto flex w-full max-w-5xl flex-col gap-6 px-4 py-8">
    <header class="space-y-1">
      <h1 class="text-2xl font-semibold">
        MatchRow playground
      </h1>
      <p class="text-sm text-muted">
        Isolated visual review for <code>MatchRow</code> — mock fixtures on top, optional live fetch by Riot ID below.
        Collapsed accordion only; expanded view tabs are deferred.
      </p>
    </header>

    <section class="flex flex-col gap-2">
      <h2 class="text-lg font-semibold">
        Mock fixtures
      </h2>
      <template v-if="staticBundleReady">
        <MatchRow
          v-for="match in mockMatches"
          :key="match.matchId"
          :match="match"
          :champions="champions"
          :items="items"
          :summoner-spells="summonerSpells"
          :rune-tree="runeTree"
        />
      </template>
      <template v-else>
        <MatchRowSkeleton v-for="i in 3" :key="i" />
      </template>
    </section>

    <section class="flex flex-col gap-2">
      <h2 class="text-lg font-semibold">
        Live data
      </h2>
      <div class="flex gap-2">
        <UInput
          v-model="nameTagInput"
          placeholder="GameName-TagLine"
          class="flex-1"
          @keyup.enter="applyLive"
        />
        <UButton color="primary" :disabled="!nameTagInput.trim()" @click="applyLive">
          Fetch
        </UButton>
      </div>

      <template v-if="!liveNameTag">
        <p class="text-sm text-muted">
          Enter a Riot ID (e.g. <code>Phantasm-EUW1</code>) to fetch live matches from
          <code>/api/truemains/{nameTag}/matches</code>.
        </p>
      </template>
      <template v-else-if="liveIsInitialLoading">
        <MatchRowSkeleton v-for="i in 3" :key="`live-skel-${i}`" />
      </template>
      <template v-else-if="liveNotFound">
        <MatchHistoryEmpty not-found />
      </template>
      <template v-else-if="liveMatches.length === 0">
        <MatchHistoryEmpty />
      </template>
      <template v-else>
        <MatchRow
          v-for="match in liveMatches"
          :key="match.matchId"
          :match="match"
          :champions="champions"
          :items="items"
          :summoner-spells="summonerSpells"
          :rune-tree="runeTree"
        />
        <UButton
          v-if="liveHasMore"
          variant="ghost"
          :loading="liveIsLoading"
          @click="liveLoadMore()"
        >
          Load more
        </UButton>
      </template>
    </section>
  </div>
</template>
