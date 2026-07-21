<script setup lang="ts">
import type { ChampionPosition } from '~/utils/positions'
import { parseRouteParam } from '~/utils/route-params'

const route = useRoute()

const nameTag = computed(() => parseRouteParam(route.params.nameTag))

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
} = useTruemainProfile(nameTag)

const {
  data: rankHistory,
  isInitialLoading: rankHistoryLoading,
} = useTruemainRankHistory(nameTag)

// Human label for the breadcrumb / SEO title — `gameName#tagLine`, falling
// back to the raw nameTag slug while the profile fetch is in flight.
const playerLabel = computed(() => {
  const identity = profile.value?.identity
  if (!identity) return nameTag.value
  return identity.tagLine ? `${identity.gameName}#${identity.tagLine}` : identity.gameName
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
// Shared canonical cache keys (`champion-static-list`, `static-items-*`,
// `static-summoners-*`, `rune-tree-*`) so the payloads warmed by the
// champion pages / prefetch plugin are reused here instead of refetched
// under profile-specific keys.
const { data: versions } = useDDragonVersions()
const latestPatch = computed(() => versions.value?.[0] ?? null)

const { data: championsData } = useChampionStaticList()
const champions = computed(() => championsData.value ?? [])

const { data: itemsData } = useStaticItems(latestPatch)
const items = computed(() => itemsData.value ?? {})

const { data: summonerSpellsData } = useStaticSummonerSpells(latestPatch)
const summonerSpells = computed(() => summonerSpellsData.value ?? {})

const { data: runeTree } = useStaticRuneTree(latestPatch)

const staticBundleReady = computed(() =>
  champions.value.length > 0
  && Object.keys(items.value).length > 0
  && Object.keys(summonerSpells.value).length > 0
  && (runeTree.value?.styles.length ?? 0) > 0,
)

const hasActiveFilters = computed(() => Boolean(filterPosition.value || filterChampionId.value))
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
    <!-- Truemains > {player}, linking back to the OTP leaderboard. -->
    <UBreadcrumb :items="breadcrumbItems" class="mb-6" />

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
        <ProfileRankedCard
          v-else
          :ranked="profile.ranked"
          :history="rankHistory?.entries ?? []"
          :history-loading="rankHistoryLoading"
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

        <!--
          The empty / not-found state must not wait on the static bundle:
          rendering it needs no item, spell or rune data, and a failing
          static fetch (e.g. CDragon lagging a new patch) would otherwise
          keep the skeletons up forever on a perfectly valid empty result.
        -->
        <template v-if="matchesInitialLoading">
          <MatchRowSkeleton v-for="i in MATCHES_PAGE_SIZE" :key="`match-skel-${i}`" />
        </template>
        <template v-else-if="matchesNotFound || matches.length === 0">
          <MatchHistoryEmpty :not-found="matchesNotFound" :filtered="hasActiveFilters" />
        </template>
        <template v-else-if="!staticBundleReady">
          <MatchRowSkeleton v-for="i in MATCHES_PAGE_SIZE" :key="`match-skel-${i}`" />
        </template>
        <template v-else>
          <MatchRow
            v-for="match in matches"
            :key="match.matchId"
            :match="match"
            :champions="champions"
            :items="items"
            :summoner-spells="summonerSpells"
            :rune-tree="runeTree!"
            :name-tag="nameTag"
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
