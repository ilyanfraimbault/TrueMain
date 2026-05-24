<script setup lang="ts">
import type {
  ChampionStaticListItem,
  RuneTreeResponse,
  StaticItemData,
  StaticSummonerSpellData,
} from '~~/shared/types/static-data'
import { isChampionPosition, type ChampionPosition } from '~/utils/positions'

const route = useRoute()
const router = useRouter()

const nameTag = computed(() => {
  const param = route.params.nameTag
  return Array.isArray(param) ? param[0] ?? '' : (param ?? '')
})

const MATCHES_PAGE_SIZE = 20

// 1-indexed current page, sourced from `?page=` so back/forward + direct
// links stay in sync with the matches feed. Same coercion as
// pages/champions/index.vue — invalid values clamp to page 1.
const currentMatchesPage = computed<number>(() => {
  const raw = Array.isArray(route.query.page) ? route.query.page[0] : route.query.page
  const parsed = Number.parseInt(typeof raw === 'string' ? raw : '', 10)
  return Number.isFinite(parsed) && parsed >= 1 ? parsed : 1
})

// Filters live alongside the page in the URL so they're shareable. Position
// is the Riot uppercase enum (TOP/JUNGLE/...); the helper guards against
// garbage values so an attacker-controlled query never reaches the API.
const filterPosition = computed<ChampionPosition | null>(() => {
  const raw = Array.isArray(route.query.position) ? route.query.position[0] : route.query.position
  return isChampionPosition(raw) ? raw : null
})

const filterChampionId = computed<number | null>(() => {
  const raw = Array.isArray(route.query.championId) ? route.query.championId[0] : route.query.championId
  const parsed = Number.parseInt(typeof raw === 'string' ? raw : '', 10)
  return Number.isFinite(parsed) && parsed > 0 ? parsed : null
})

async function setMatchesPage(next: number) {
  const clamped = Math.max(1, Math.floor(next))
  if (clamped === currentMatchesPage.value) return
  const nextQuery = { ...route.query }
  if (clamped === 1) delete nextQuery.page
  else nextQuery.page = String(clamped)
  await router.replace({ query: nextQuery })
}

// Filter mutations always reset the page back to 1 — staying on, say,
// page 5 after switching to "MID only" risks landing on an out-of-range
// page since the total just shrank.
async function setFilterPosition(next: ChampionPosition | null) {
  const nextQuery = { ...route.query }
  if (next) nextQuery.position = next
  else delete nextQuery.position
  delete nextQuery.page
  await router.replace({ query: nextQuery })
}

async function setFilterChampionId(next: number | null) {
  const nextQuery = { ...route.query }
  if (next) nextQuery.championId = String(next)
  else delete nextQuery.championId
  delete nextQuery.page
  await router.replace({ query: nextQuery })
}

// ─── Profile fetch ─────────────────────────────────────────────────────────
const {
  data: profile,
  isInitialLoading: profileLoading,
  notFound: profileNotFound,
} = useTruemainProfile(nameTag)

useSeoMeta({
  title: () => {
    const identity = profile.value?.identity
    if (!identity) return `${nameTag.value} · TrueMain`
    const display = identity.tagLine ? `${identity.gameName}#${identity.tagLine}` : identity.gameName
    return `${display} · TrueMain`
  },
  description: () => {
    const identity = profile.value?.identity
    if (!identity) return 'TrueMain player profile.'
    return `Recent matches, main champions and ranked progress for ${identity.gameName} on ${identity.platformId}.`
  },
})

// ─── Matches fetch ─────────────────────────────────────────────────────────
const {
  matches,
  total: matchesTotal,
  pageSize: matchesPageSize,
  isInitialLoading: matchesInitialLoading,
  notFound: matchesNotFound,
} = useTruemainMatches(nameTag, currentMatchesPage, {
  pageSize: MATCHES_PAGE_SIZE,
  position: filterPosition,
  championId: filterChampionId,
})

// ─── Static lookups for MatchRow + identity icon ───────────────────────────
const { data: versions } = useDDragonVersions()
const latestPatch = computed(() => versions.value?.[0] ?? null)

const nuxtApp = useNuxtApp()

const { data: champions } = useLazyAsyncData<ChampionStaticListItem[]>(
  'truemain-profile-champions',
  async () => {
    const key = 'truemain-profile-champions'
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
  () => `truemain-profile-items-${latestPatch.value ?? 'none'}`,
  async () => {
    const patch = latestPatch.value ?? ''
    const key = `truemain-profile-items-${patch || 'none'}`
    const data = await $fetch<Record<number, StaticItemData>>('/api/static/items', {
      query: { patch },
    })
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
  () => `truemain-profile-summoner-spells-${latestPatch.value ?? 'none'}`,
  async () => {
    const patch = latestPatch.value ?? ''
    const key = `truemain-profile-summoner-spells-${patch || 'none'}`
    const data = await $fetch<Record<number, StaticSummonerSpellData>>('/api/static/summoner-spells', {
      query: { patch },
    })
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
  () => `truemain-profile-rune-tree-${latestPatch.value ?? 'none'}`,
  async () => {
    const patch = latestPatch.value ?? ''
    const key = `truemain-profile-rune-tree-${patch || 'none'}`
    const data = await $fetch<RuneTreeResponse>('/api/static/rune-tree', {
      query: { patch },
    })
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

const staticBundleReady = computed(() =>
  champions.value.length > 0
  && Object.keys(items.value).length > 0
  && Object.keys(summonerSpells.value).length > 0
  && (runeTree.value?.styles.length ?? 0) > 0,
)
</script>

<template>
  <!--
    Two-column layout on lg+. Left rail (20rem) collects the player-level
    summary (identity, ranked, mains, roles); the right rail is the match
    feed and stretches into the rest of the viewport. On smaller screens
    everything stacks naturally — the grid collapses to a single column.

    The container caps at 7xl (1280px); going wider starts to feel sparse
    on ultrawide screens where the match rows can't get any denser without
    more data per row.
  -->
  <main class="mx-auto w-full max-w-7xl p-4 md:p-6">
    <template v-if="profileNotFound">
      <ProfileNotFound :name-tag="nameTag" />
    </template>
    <div v-else class="grid gap-6 lg:grid-cols-[20rem_minmax(0,1fr)]">
      <!-- Left rail: identity + ranked + mains + roles -->
      <aside class="flex flex-col gap-4">
        <ProfileHeaderSkeleton v-if="profileLoading || !profile" />
        <ProfileHeader
          v-else
          :identity="profile.identity"
          :patch="latestPatch"
        />

        <ProfileRankedCardSkeleton v-if="profileLoading || !profile" />
        <ProfileRankedCard v-else :ranked="profile.ranked" />

        <ProfileMainChampionsSkeleton v-if="profileLoading || !profile" />
        <ProfileMainChampions
          v-else-if="profile.mains.length > 0"
          :mains="profile.mains"
          :champions="champions"
        />

        <ProfilePositionBreakdownSkeleton v-if="profileLoading || !profile" />
        <ProfilePositionBreakdown
          v-else
          :positions="profile.positions"
        />
      </aside>

      <!-- Right rail: paginated match history -->
      <section class="flex min-w-0 flex-col gap-3">
        <div class="flex flex-wrap items-center justify-between gap-2">
          <h2 class="text-xs font-semibold uppercase tracking-wide text-muted">
            Match history
          </h2>
          <MatchHistoryFilters
            :champions="champions"
            :position="filterPosition"
            :champion-id="filterChampionId"
            @update:position="setFilterPosition"
            @update:champion-id="setFilterChampionId"
          />
        </div>

        <template v-if="matchesInitialLoading || !staticBundleReady">
          <MatchRowSkeleton v-for="i in MATCHES_PAGE_SIZE" :key="`match-skel-${i}`" />
        </template>
        <template v-else-if="matchesNotFound || matches.length === 0">
          <MatchHistoryEmpty :not-found="matchesNotFound" />
        </template>
        <template v-else>
          <MatchRow
            v-for="match in matches"
            :key="match.matchId"
            :match="match"
            :champions="champions"
            :items="items"
            :summoner-spells="summonerSpells"
            :rune-tree="runeTree"
          />
          <div
            v-if="matchesTotal > matchesPageSize"
            class="flex justify-center pt-2"
          >
            <UPagination
              :page="currentMatchesPage"
              :total="matchesTotal"
              :items-per-page="matchesPageSize"
              :sibling-count="1"
              color="neutral"
              variant="ghost"
              active-color="primary"
              active-variant="soft"
              @update:page="setMatchesPage"
            />
          </div>
        </template>
      </section>
    </div>
  </main>
</template>
