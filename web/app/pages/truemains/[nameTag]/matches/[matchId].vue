<script setup lang="ts">
import type {
  ChampionStaticListItem,
  RuneTreeResponse,
  StaticItemData,
  StaticSummonerSpellData,
} from '~~/shared/types/static-data'
import { getQueueLabel } from '~/utils/queues'
import { formatDuration, formatRelativeTime } from '~/utils/relativeTime'

const route = useRoute()

// Safe param parsing — route params can be `string | string[]`; guard the
// array case the same way the profile page does.
const nameTag = computed(() => {
  const param = route.params.nameTag
  return Array.isArray(param) ? param[0] ?? '' : (param ?? '')
})
const matchId = computed(() => {
  const param = route.params.matchId
  return Array.isArray(param) ? param[0] ?? '' : (param ?? '')
})

const { data: detail, isLoading, notFound } = useMatchDetail(nameTag, matchId)

useSeoMeta({
  title: () => `Match ${matchId.value} · TrueMain`,
  description: () => `Item build timeline, skill order and scoreboard for match ${matchId.value}.`,
})

// ─── Static lookups (shared caches with the rest of the app) ───────────────
const { data: versions } = useDDragonVersions()
const latestPatch = computed(() => versions.value?.[0] ?? null)

const nuxtApp = useNuxtApp()

const { data: champions } = useLazyAsyncData<ChampionStaticListItem[]>(
  'match-detail-champions',
  async () => {
    const key = 'match-detail-champions'
    const data = await $fetch<ChampionStaticListItem[]>('/api/static/champions')
    markStaticFetched(key, nuxtApp)
    return data
  },
  {
    default: () => [],
    server: false,
    getCachedData: key => getStaticCachedData(key, nuxtApp),
  },
)

const { data: items } = useLazyAsyncData<Record<number, StaticItemData>>(
  () => `match-detail-items-${latestPatch.value ?? 'none'}`,
  async () => {
    const patch = latestPatch.value ?? ''
    const key = `match-detail-items-${patch || 'none'}`
    const data = await $fetch<Record<number, StaticItemData>>('/api/static/items', { query: { patch } })
    markStaticFetched(key, nuxtApp)
    return data
  },
  {
    default: () => ({}),
    server: false,
    watch: [latestPatch],
    getCachedData: key => getStaticCachedData(key, nuxtApp),
  },
)

const { data: summonerSpells } = useLazyAsyncData<Record<number, StaticSummonerSpellData>>(
  () => `match-detail-summoner-spells-${latestPatch.value ?? 'none'}`,
  async () => {
    const patch = latestPatch.value ?? ''
    const key = `match-detail-summoner-spells-${patch || 'none'}`
    const data = await $fetch<Record<number, StaticSummonerSpellData>>('/api/static/summoner-spells', { query: { patch } })
    markStaticFetched(key, nuxtApp)
    return data
  },
  {
    default: () => ({}),
    server: false,
    watch: [latestPatch],
    getCachedData: key => getStaticCachedData(key, nuxtApp),
  },
)

const { data: runeTree } = useLazyAsyncData<RuneTreeResponse>(
  () => `match-detail-rune-tree-${latestPatch.value ?? 'none'}`,
  async () => {
    const patch = latestPatch.value ?? ''
    const key = `match-detail-rune-tree-${patch || 'none'}`
    const data = await $fetch<RuneTreeResponse>('/api/static/rune-tree', { query: { patch } })
    markStaticFetched(key, nuxtApp)
    return data
  },
  {
    default: () => ({ styles: [], perks: {}, perkStyles: {}, shardSlots: [] }),
    server: false,
    watch: [latestPatch],
    getCachedData: key => getStaticCachedData(key, nuxtApp),
  },
)

// ─── Derived view state ─────────────────────────────────────────────────────
const blueTeam = computed(() => detail.value?.participants.filter(p => p.teamId === 100) ?? [])
const redTeam = computed(() => detail.value?.participants.filter(p => p.teamId === 200) ?? [])

const blueWin = computed(() => blueTeam.value[0]?.win ?? false)

// The header banner reads the winning side off whichever team actually won,
// rather than assuming blue exists — a remake or odd payload with no team-100
// rows would otherwise always label the game a red victory. Null when neither
// side reports a win (e.g. an empty/remade match) so the banner is hidden.
const winningSide = computed<'blue' | 'red' | null>(() => {
  if (blueTeam.value.some(p => p.win)) return 'blue'
  if (redTeam.value.some(p => p.win)) return 'red'
  return null
})

const headerMeta = computed(() => {
  if (!detail.value) return null
  return {
    queue: getQueueLabel(detail.value.queueId, detail.value.gameMode),
    duration: formatDuration(detail.value.gameDurationSeconds),
    when: formatRelativeTime(detail.value.gameStartTimeUtc),
    version: detail.value.gameVersion,
  }
})

const tabItems = [
  { value: 'general', label: 'General', slot: 'general' as const },
  { value: 'details', label: 'Details', slot: 'details' as const },
  { value: 'runes', label: 'Runes', slot: 'runes' as const },
]
</script>

<template>
  <main class="mx-auto w-full max-w-7xl p-4 md:p-6">
    <div class="mb-4">
      <NuxtLink
        :to="`/truemains/${encodeURIComponent(nameTag)}`"
        class="text-xs font-medium text-muted hover:text-default"
      >
        ← Back to profile
      </NuxtLink>
    </div>

    <template v-if="isLoading && !detail">
      <div class="glass rounded-md border border-default/60 bg-elevated/40 p-8 text-center text-sm text-muted">
        Loading match…
      </div>
    </template>

    <template v-else-if="notFound || !detail">
      <div class="glass rounded-md border border-default/60 bg-elevated/40 p-8 text-center">
        <h1 class="text-base font-semibold text-default">
          Match not found
        </h1>
        <p class="mt-1 text-sm text-muted">
          We couldn't find this match for {{ nameTag }}.
        </p>
      </div>
    </template>

    <template v-else>
      <!-- Header -->
      <header
        class="glass mb-4 flex flex-wrap items-center gap-x-4 gap-y-1 rounded-md border border-default/60 bg-elevated/40 px-4 py-3"
      >
        <span
          v-if="winningSide"
          class="text-sm font-bold"
          :class="winningSide === 'blue' ? 'text-sky-400' : 'text-red-400'"
        >
          {{ winningSide === 'blue' ? 'Blue victory' : 'Red victory' }}
        </span>
        <span v-if="headerMeta" class="text-xs text-muted">{{ headerMeta.queue }}</span>
        <span v-if="headerMeta" class="text-xs text-muted">{{ headerMeta.duration }}</span>
        <span v-if="headerMeta" class="text-xs text-muted">{{ headerMeta.when }}</span>
        <span v-if="headerMeta" class="ml-auto text-xs text-muted/70">Patch {{ headerMeta.version }}</span>
      </header>

      <UTabs
        :items="tabItems"
        default-value="general"
        variant="link"
        class="w-full"
        :unmount-on-hide="false"
      >
        <!-- ── General: scoreboard ─────────────────────────────────────── -->
        <template #general>
          <div class="mt-3 flex flex-col gap-3">
            <MatchDetailScoreboard
              :participants="blueTeam"
              :team-id="100"
              :win="blueWin"
              :champions="champions"
              :items="items"
              :summoner-spells="summonerSpells"
              :rune-tree="runeTree"
            />
            <MatchDetailScoreboard
              :participants="redTeam"
              :team-id="200"
              :win="!blueWin"
              :champions="champions"
              :items="items"
              :summoner-spells="summonerSpells"
              :rune-tree="runeTree"
            />
          </div>
        </template>

        <!-- ── Details: per-player build + lane ────────────────────────── -->
        <template #details>
          <div class="mt-3 grid gap-3 lg:grid-cols-2">
            <MatchDetailPlayerCard
              v-for="p in detail.participants"
              :key="`detail-${p.participantId}`"
              :participant="p"
              :champions="champions"
              :items="items"
            />
          </div>
        </template>

        <!-- ── Runes: full rune page per player ────────────────────────── -->
        <template #runes>
          <div class="mt-3 grid gap-3 lg:grid-cols-2">
            <MatchDetailRunePage
              v-for="p in detail.participants"
              :key="`runes-${p.participantId}`"
              :participant="p"
              :champions="champions"
              :rune-tree="runeTree"
            />
          </div>
        </template>
      </UTabs>
    </template>
  </main>
</template>
