<script setup lang="ts">
import type { ChampionPosition } from '#common/utils/positions'
import { parseRouteParam } from '#common/utils/route-params'
import { championSplashUrlFromIcon } from '~~/shared/utils/ddragon'
import { platformIdToRegion } from '~~/shared/utils/region'

const route = useRoute()

const nameTag = computed(() => parseRouteParam(route.params.nameTag))

// A 404 for an unknown account, and the stored Riot ID's spelling as the
// canonical (#2335, #2336).
const indexingReady = useTruemainPageIndexing(nameTag, truemainProfilePath)

const MATCHES_PAGE_SIZE = 20

// URL state for the matches feed — page, position and champion filters are
// shareable and survive back/forward. Shared coercion helpers with the other
// list pages: invalid values fall back to "no filter" / page 1 so an
// attacker-controlled query never reaches the API.
const { currentPage: currentMatchesPage, setPage: setMatchesPage } = useRoutePage()

const filterPosition = useRouteQueryPosition()
const filterChampionId = useRouteQueryChampionId()

// Filter mutations always reset the page back to 1 — staying on, say,
// page 5 after switching to "MID only" risks landing on an out-of-range
// page since the total just shrank (see useRouteFilterSetter).
const setQueryFilter = useRouteFilterSetter()

async function setFilterPosition(next: ChampionPosition | null) {
  await setQueryFilter('position', next)
}

async function setFilterChampionId(next: number | null) {
  await setQueryFilter('championId', next ? String(next) : null)
}

// ─── Profile fetch ─────────────────────────────────────────────────────────
const {
  data: profile,
  isInitialLoading: profileLoading,
  notFound: profileNotFound,
  ready: profileReady,
} = useTruemainProfile(nameTag)

const {
  data: rankHistory,
  isInitialLoading: rankHistoryLoading,
  ready: rankHistoryReady,
} = useTruemainRankHistory(nameTag)
// The region's GM / Challenger lines on the LP curve (#2174).
const apexCutoffs = useApexCutoffs(() => profile.value?.identity.platformId)

// Activity grid under the LP curve (#927). One request covers all four
// granularities — the mode switch inside the card is local, so no refetch and no
// chance of two modes describing different snapshots.
const {
  data: activity,
  isInitialLoading: activityLoading,
  ready: activityReady,
} = useTruemainActivity(nameTag)

// Human label for the breadcrumb / SEO title — `gameName#tagLine`. While the
// (client-only) profile fetch is in flight it is derived from the route slug
// alone, so the SSR `<title>` already reads `Name#TAG` and the client's first
// render computes the same value (#948, the hydration trap of #862).
const playerLabel = computed(() => {
  const identity = profile.value?.identity
  if (!identity) return truemainSlugLabel(nameTag.value)
  return riotIdLabel(identity.gameName, identity.tagLine)
})

// Truemains > {player}. Rendered even in the loading / not-found states so the
// page always has a way back up to the leaderboard.
const breadcrumbItems = computed(() => [
  { label: 'Truemains', to: '/truemains' },
  { label: playerLabel.value },
])

useSeoMeta({
  title: () => playerLabel.value,
  description: () => {
    const identity = profile.value?.identity
    if (!identity) return 'TrueMain player profile.'
    return `Recent matches, main champions and ranked progress for ${identity.gameName} on ${identity.platformId}.`
  },
})

// Dynamic share card (#926). The profile itself is fetched client-only by
// design (`useTruemainFetch` — no SSR cross-pollination between viewers), so
// the only thing available when this og:image URL is minted is the route slug.
// The card resolves the profile itself through `/api/og/truemain/{nameTag}` at
// render time; it is a public rendering of a public profile, so nothing
// viewer-specific leaks into a shared image.
defineOgImage('Truemain', { nameTag })

// `playerLabel` is derived from the slug while the profile is in flight, so
// the share text is always something a human can read.
const shareTitle = computed(() => `${playerLabel.value} on TrueMain`)
const SHARE_DESCRIPTION = 'Rank, main champions and Truemain score — tracked as a true main.'

// ─── Matches fetch ─────────────────────────────────────────────────────────
const {
  matches,
  total: matchesTotal,
  pageSize: matchesPageSize,
  isInitialLoading: matchesInitialLoading,
  notFound: matchesNotFound,
  ready: matchesReady,
} = useTruemainMatches(nameTag, currentMatchesPage, {
  pageSize: MATCHES_PAGE_SIZE,
  position: filterPosition,
  championId: filterChampionId,
})

// ─── Static lookups for MatchRow + identity icon ───────────────────────────
// Shared canonical cache keys (`champion-static-list`, `static-items-*`,
// `static-summoners-*`, `rune-tree-*`) so the payloads warmed by the
// champion pages / prefetch plugin are reused here instead of refetched
// under profile-specific keys.
const { data: versions } = useDDragonVersions()
const latestPatch = computed(() => versions.value?.[0] ?? null)

const { data: championsData } = useChampionStaticList()
const champions = computed(() => championsData.value ?? [])

const { data: items } = useStaticItems(latestPatch)
const { data: summonerSpells } = useStaticSummonerSpells(latestPatch)
const { data: runeTree } = useStaticRuneTree(latestPatch)

// The banner: the champion this player truemains, on its splash through the
// image proxy at banner width.
const splashOf = useCanonicalSplash()
const { truemainChampion, identity: heroIdentity } = useTruemainBanner(profile, champions, playerLabel)
const splashUrl = computed(() => splashOf(championSplashUrlFromIcon(truemainChampion.value?.iconUrl)))

const hasActiveFilters = computed(() => Boolean(filterPosition.value || filterChampionId.value))

// A client-side navigation keeps the outgoing page under the loading bar until
// the API has answered, so this page opens on its data (#1689). Resolves at
// once on a server render and a hydration, which keep their skeletons.
await Promise.all([profileReady, rankHistoryReady, activityReady, matchesReady, indexingReady])
</script>

<template>
  <div class="mx-auto w-full max-w-7xl p-4 md:p-6">
    <template v-if="profileNotFound">
      <!-- Truemains > {player}. Share controls (#926) stay in every state:
           the URL is shareable whether or not the profile resolved. -->
      <div class="mb-6 flex flex-wrap items-center justify-between gap-3">
        <UBreadcrumb :items="breadcrumbItems" />
        <ShareButtons
          :title="shareTitle"
          :description="SHARE_DESCRIPTION"
        />
      </div>
      <ProfileNotFound :name-tag="nameTag" />
    </template>

    <!-- The shared profile (the desktop dashboard's layout): banner on the
         champion this player truemains, the form tiles, the history, and the
         player-level cards in the 18rem column. -->
    <PagePlayerProfile
      v-else
      :identity="heroIdentity"
      :patch="latestPatch"
      :level="profile?.identity.summonerLevel ?? null"
      :ranked="profile?.ranked ?? null"
      :truemain="truemainChampion"
      :persona="profile?.persona ?? null"
      :splash-url="splashUrl"
      splash-position="62% 22%"
      :matches="matches"
      :loading="matchesInitialLoading"
    >
      <template #header>
        <div class="flex flex-wrap items-center justify-between gap-3">
          <UBreadcrumb :items="breadcrumbItems" />
          <MatchHistoryFilters
            :champions="champions"
            :position="filterPosition"
            :champion-id="filterChampionId"
            @update:position="setFilterPosition"
            @update:champion-id="setFilterChampionId"
          />
        </div>
      </template>

      <template
        v-if="profile"
        #title-actions
      >
        <FavoriteToggle
          :game-name="profile.identity.gameName"
          :tag-line="profile.identity.tagLine"
          :region="platformIdToRegion(profile.identity.platformId)"
          :profile-icon-id="profile.identity.profileIconId"
        />
      </template>

      <template #actions>
        <ShareButtons
          :title="shareTitle"
          :description="SHARE_DESCRIPTION"
        />
      </template>

      <template #history>
        <h2 class="text-xs font-semibold uppercase tracking-wide text-muted">
          Match history
        </h2>
        <MatchHistoryList
          :page="currentMatchesPage"
          :matches="matches"
          :loading="matchesInitialLoading"
          :total="matchesTotal"
          :page-size="matchesPageSize"
          :champions="champions"
          :items="items"
          :summoner-spells="summonerSpells"
          :rune-tree="runeTree"
          :name-tag="nameTag"
          :not-found="matchesNotFound"
          :filtered="hasActiveFilters"
          @update:page="setMatchesPage"
        />
      </template>

      <template #aside>
        <ProfileRankedCardSkeleton v-if="profileLoading || !profile" />
        <ProfileRankedCard
          v-else
          :ranked="profile.ranked"
          :history="rankHistory?.entries ?? []"
          :history-loading="rankHistoryLoading || apexCutoffs.loading"
          :cutoffs="apexCutoffs.cutoffs"
        />

        <!-- Under the ranked card: the LP curve says how the climb went, the
             grid how much was played to get there. Its own loading state is
             internal, so the window switch is usable while the payload lands. -->
        <ProfileActivityHeatmap
          v-if="!profileLoading && profile"
          :data="activity"
          :loading="activityLoading"
        />

        <ProfileDedicationCardSkeleton v-if="profileLoading || !profile" />
        <ProfileDedicationCard
          v-else-if="profile.dedication"
          :dedication="profile.dedication"
          :champions="champions"
          :name-tag="nameTag"
        />

        <ProfileMainChampionsSkeleton v-if="profileLoading || !profile" />
        <ProfileMainChampions
          v-else-if="profile.mains.length > 0"
          :mains="profile.mains"
          :champions="champions"
          :name-tag="nameTag"
        />

        <ProfilePositionBreakdownSkeleton v-if="profileLoading || !profile" />
        <ProfilePositionBreakdown
          v-else
          :positions="profile.positions"
        />
      </template>
    </PagePlayerProfile>
  </div>
</template>
