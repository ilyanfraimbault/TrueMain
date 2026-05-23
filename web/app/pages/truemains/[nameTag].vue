<script setup lang="ts">
import type {
  ChampionStaticListItem,
  RuneTreeResponse,
  StaticItemData,
  StaticSummonerSpellData,
} from '~~/shared/types/static-data'

const route = useRoute()
const nameTag = computed(() => {
  const param = route.params.nameTag
  return Array.isArray(param) ? param[0] ?? '' : (param ?? '')
})

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
  isLoading: matchesLoading,
  isInitialLoading: matchesInitialLoading,
  notFound: matchesNotFound,
  hasMore: matchesHasMore,
  loadMore: loadMoreMatches,
} = useTruemainMatches(nameTag, { pageSize: 10 })

// ─── Static lookups for MatchRow + identity icon ───────────────────────────
// Same patch-keyed pattern as pages/champions/[id].vue. We need the latest
// patch for the profile icon URL (DDragon doesn't keep older patches'
// profile icons fresh) and for the static maps.
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
  <main class="mx-auto flex w-full max-w-5xl flex-col gap-6 p-4 md:p-6">
    <template v-if="profileNotFound">
      <ProfileNotFound :name-tag="nameTag" />
    </template>
    <template v-else>
      <!-- 1. Identity -->
      <ProfileHeaderSkeleton v-if="profileLoading || !profile" />
      <ProfileHeader
        v-else
        :identity="profile.identity"
        :patch="latestPatch"
      />

      <!-- 2. Ranked -->
      <ProfileRankedCardSkeleton v-if="profileLoading || !profile" />
      <ProfileRankedCard v-else :ranked="profile.ranked" />

      <!-- 3. Main champions -->
      <ProfileMainChampionsSkeleton v-if="profileLoading || !profile" />
      <ProfileMainChampions
        v-else-if="profile.mains.length > 0"
        :mains="profile.mains"
        :champions="champions"
      />

      <!-- 4. Position breakdown -->
      <ProfilePositionBreakdownSkeleton v-if="profileLoading || !profile" />
      <ProfilePositionBreakdown
        v-else
        :positions="profile.positions"
      />

      <!-- 5. Recent matches -->
      <section class="flex flex-col gap-3">
        <h2 class="text-xs font-semibold uppercase tracking-wide text-muted">
          Recent matches
        </h2>

        <template v-if="matchesInitialLoading || !staticBundleReady">
          <MatchRowSkeleton v-for="i in 3" :key="`match-skel-${i}`" />
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
          <UButton
            v-if="matchesHasMore"
            variant="ghost"
            :loading="matchesLoading"
            class="self-center"
            @click="loadMoreMatches()"
          >
            Load more
          </UButton>
        </template>
      </section>
    </template>
  </main>
</template>
