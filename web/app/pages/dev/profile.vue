<script setup lang="ts">
import type {
  MatchSummaryResponse,
} from '~~/shared/types/matches'
import type { ProfileResponse } from '~~/shared/types/profile'
import type { RankHistoryEntry } from '~~/shared/types/rank-history'
import type {
  ChampionStaticListItem,
  RuneTreeResponse,
  StaticItemData,
  StaticSummonerSpellData,
} from '~~/shared/types/static-data'

definePageMeta({ layout: 'default' })

useSeoMeta({
  title: 'Profile playground',
  description: 'Isolated visual review of the truemain profile page with mock fixtures.',
})

// ─── Static lookups (real fetches) ─────────────────────────────────────────
const { data: versions } = useDDragonVersions()
const latestPatch = computed(() => versions.value?.[0] ?? null)

const { data: champions } = useLazyAsyncData<ChampionStaticListItem[]>(
  'dev-profile-champions',
  () => $fetch<ChampionStaticListItem[]>('/api/static/champions'),
  { default: () => [], server: false },
)
const { data: items } = useLazyAsyncData<Record<number, StaticItemData>>(
  () => `dev-profile-items-${latestPatch.value ?? ''}`,
  () => $fetch<Record<number, StaticItemData>>('/api/static/items', { query: { patch: latestPatch.value ?? '' } }),
  { default: () => ({}), server: false, watch: [latestPatch] },
)
const { data: summonerSpells } = useLazyAsyncData<Record<number, StaticSummonerSpellData>>(
  () => `dev-profile-summoner-spells-${latestPatch.value ?? ''}`,
  () => $fetch<Record<number, StaticSummonerSpellData>>('/api/static/summoner-spells', { query: { patch: latestPatch.value ?? '' } }),
  { default: () => ({}), server: false, watch: [latestPatch] },
)
const { data: runeTree } = useLazyAsyncData<RuneTreeResponse>(
  () => `dev-profile-rune-tree-${latestPatch.value ?? ''}`,
  () => $fetch<RuneTreeResponse>('/api/static/rune-tree', { query: { patch: latestPatch.value ?? '' } }),
  { default: () => ({ styles: [], perks: {}, perkStyles: {}, shardSlots: [] }), server: false, watch: [latestPatch] },
)

const staticBundleReady = computed(() =>
  champions.value.length > 0
  && Object.keys(items.value).length > 0
  && Object.keys(summonerSpells.value).length > 0
  && (runeTree.value?.styles.length ?? 0) > 0,
)

// ─── Mock profile + matches ────────────────────────────────────────────────
const mockProfile: ProfileResponse = {
  identity: {
    gameName: 'Phantasm',
    tagLine: 'EUW1',
    platformId: 'EUW1',
    profileIconId: 4567,
    summonerLevel: 312,
  },
  ranked: {
    tier: 'DIAMOND',
    division: 'II',
    leaguePoints: 72,
    wins: 90,
    losses: 60,
    winRate: 0.6,
  },
  mains: [
    { championId: 157, games: 80, playRate: 0.4, primaryPosition: 'MIDDLE', isOtp: false },
    { championId: 103, games: 60, playRate: 0.3, primaryPosition: 'MIDDLE', isOtp: false },
    { championId: 99, games: 30, playRate: 0.15, primaryPosition: 'MIDDLE', isOtp: false },
    { championId: 222, games: 20, playRate: 0.1, primaryPosition: 'BOTTOM', isOtp: false },
  ],
  positions: [
    { position: 'MIDDLE', games: 170, rate: 170 / 190 },
    { position: 'BOTTOM', games: 20, rate: 20 / 190 },
  ],
}

const now = new Date()
function isoMinutesAgo(minutes: number): string {
  return new Date(now.getTime() - minutes * 60 * 1000).toISOString()
}

// 60 daily-ish snapshots tracing a wide climb across multiple tiers so the
// chart shows real tier crossings and the 30d / 7d delta badges have data.
const mockRankHistory: RankHistoryEntry[] = (() => {
  const entries: RankHistoryEntry[] = []
  const path: Array<{ tier: string, division: string, lp: number }> = [
    { tier: 'EMERALD', division: 'III', lp: 40 },
    { tier: 'EMERALD', division: 'II', lp: 60 },
    { tier: 'EMERALD', division: 'I', lp: 75 },
    { tier: 'DIAMOND', division: 'IV', lp: 20 },
    { tier: 'DIAMOND', division: 'III', lp: 45 },
    { tier: 'DIAMOND', division: 'II', lp: 72 },
  ]
  for (let day = 59; day >= 0; day--) {
    const t = 1 - day / 59
    const idx = Math.min(path.length - 1, Math.floor(t * path.length))
    const next = path[idx]!
    const lp = Math.max(0, Math.min(99, next.lp + Math.round(Math.sin(day / 4) * 8)))
    entries.push({
      capturedAtUtc: new Date(now.getTime() - day * 24 * 60 * 60 * 1000).toISOString(),
      tier: next.tier,
      division: next.division,
      leaguePoints: lp,
    })
  }
  // Pin the final entry to the headline rank so the chart endpoint matches.
  entries[entries.length - 1] = {
    capturedAtUtc: now.toISOString(),
    tier: 'DIAMOND',
    division: 'II',
    leaguePoints: 72,
  }
  return entries
})()

function makeMockParticipants(): MatchSummaryResponse['participants'] {
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
      championId: 103,
      championLevel: 17,
      summoner1Id: 4,
      summoner2Id: 7,
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
      teamId: 200,
      win: false,
      lpDelta: -18,
      isMvp: false,
      isAce: false,
    },
    participants: makeMockParticipants(),
  },
])
</script>

<template>
  <main class="mx-auto flex w-full max-w-5xl flex-col gap-6 p-4 md:p-6">
    <header class="flex flex-col gap-1">
      <p class="text-xs font-semibold uppercase tracking-wide text-muted">
        Dev playground
      </p>
      <h1 class="text-2xl font-semibold">
        Profile page fixture
      </h1>
      <p class="text-sm text-muted">
        Renders <code>/truemains/{nameTag}.vue</code> against mock data. Use <code>/truemains/Phantasm-EUW1</code> with a running backend for the real thing.
      </p>
    </header>

    <ProfileHeader :identity="mockProfile.identity" :patch="latestPatch" />
    <ProfileRankedCard :ranked="mockProfile.ranked" :history="mockRankHistory" />
    <ProfileMainChampions :mains="mockProfile.mains" :champions="champions" name-tag="Phantasm-EUW1" />
    <ProfilePositionBreakdown :positions="mockProfile.positions" />

    <section class="flex flex-col gap-3">
      <h2 class="text-xs font-semibold uppercase tracking-wide text-muted">
        Recent matches
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
        <MatchRowSkeleton v-for="i in 2" :key="i" />
      </template>
    </section>
  </main>
</template>
