<script setup lang="ts">
import { isLoadingStatus } from '#common/utils/async-data'
import { parseRouteParam } from '#common/utils/route-params'
import { ELO_BRACKET_ALL } from '#common/utils/elo-brackets'
import { ALL_POSITIONS, type ChampionPosition, type PositionFilter } from '#common/utils/positions'
import { platformIdToRegion } from '~~/shared/utils/region'
import type { ChampionStaticListItem } from '~~/shared/types/static-data'

// Player-scoped mirror of pages/champions/[slug].vue: same static fetches,
// loading bar and build tabs; the ONLY difference is that useChampion is given
// the route's nameTag, swapping the source to /api/truemains/{nameTag}/champions/{id}.
// Keeping the static fetch keys aligned with the global champion page and the
// profile page keeps Nuxt's patch-keyed caches deduped across navigations.
const route = useRoute()

// Same slug scheme and the same guard as the global page (#1124) — the two are
// mirrors, so a URL shape that held on one and not the other would be exactly
// the kind of half-converted scheme that rots. `nameTag` is read back from the
// route rather than closed over, since the guard runs before setup.
definePageMeta({
  middleware: to => championRouteGuard(
    to,
    segment => `/truemains/${encodeURIComponent(String(to.params.nameTag))}/champions/${segment}`,
  ),
})

const { resolveParam, pathFor } = useChampionSlugs()
const championId = computed(() => resolveParam(String(route.params.slug)).championId ?? Number.NaN)
const nameTag = computed(() => parseRouteParam(route.params.nameTag))

// `ALL`, not the global pages' Master+ default: this page's scope is already
// one account, and re-slicing a single truemain's games by rank empties the
// build of everyone below Master. The page renders no rank selector either.
const { filters, setFilter } = useChampionFilters({ defaultEloBracket: ELO_BRACKET_ALL })

const {
  data: champion,
  error: championError,
  staleError: championStaleError,
  refresh: refreshChampion,
  status: championStatus,
  notEnoughData,
  ready: championReady,
} = useChampion(championId, filters, { nameTag })

// A 404 is the "not enough games" empty state below, never an error. A failed
// first load is inline only (#1661); a failed filter change keeps the slice (#1668).

// Identity for the banner and the breadcrumb. Cheap and client-cached — the
// profile page primes the same request, so this rarely hits the network.
const { data: profile, ready: profileReady } = useTruemainProfile(nameTag)

// The LP curve under the identity, same card as the profile page: about the
// account, not the champion, so nothing here is scoped by championId.
const {
  data: rankHistory,
  isInitialLoading: rankHistoryLoading,
  ready: rankHistoryReady,
} = useTruemainRankHistory(nameTag)
const playerLabel = computed(() => {
  const identity = profile.value?.identity
  if (!identity) return truemainSlugLabel(nameTag.value) // pre-fetch: from the route alone (#948)
  return riotIdLabel(identity.gameName, identity.tagLine)
})
// Same identity, without the tag line, for inline copy ("Faker vs mains").
// Derived from the slug while the profile fetch is in flight, like `playerLabel`.
const playerName = computed(() => profile.value?.identity?.gameName ?? parseTruemainNameTag(nameTag.value)?.gameName ?? nameTag.value)
const profilePath = computed(() => truemainProfilePath(nameTag.value))

// A 404 for an unknown account, and the stored Riot ID's spelling as the
// canonical — the route's slug is already canonical, its guard ran (#2335, #2336).
const indexingReady = useTruemainPageIndexing(
  nameTag,
  tag => `${truemainProfilePath(tag)}/champions/${parseRouteParam(route.params.slug)}`,
)

// Shared static-data plumbing (see useChampionDetailStatics). This page
// prefers the URL filter over the API-returned patch in `selectedPatch` —
// the historical behaviour of the player-scoped page, deliberately not
// unified with the global page's API-first order.
const {
  versions,
  staticData,
  staticStatus,
  staticList,
  staticListStatus,
  runeTree,
  runeTreeStatus,
  itemsMap,
  itemsStatus,
  summonersMap,
  summonersStatus,
  displayName,
  displayIconUrl,
  patchOptions,
  selectedPatch,
  selectedPosition,
} = useChampionDetailStatics(championId, champion, filters, {
  preferFilterPatch: true,
  championSettled: () => !isLoadingStatus(championStatus.value),
})

const splashUrl = useChampionSplash(displayIconUrl)

// "All positions" (#2158) is an explicit `?position=ALL`, distinct from no filter,
// which the API resolves to the player's dominant position. `selectedPosition` has
// no value for it, so the slice the page reads carries it on its own; the picker
// lights its All button on `null`.
const isAllPositions = computed(() => filters.value.position === ALL_POSITIONS)
const slicePosition = computed<PositionFilter | null>(() =>
  isAllPositions.value ? ALL_POSITIONS : selectedPosition.value)

// SSR-safe champion name for `<head>` — same composable, same cache key as the
// global champion page, so the two pages share the entry. See
// useChampionSeoName for why `displayName` can't serve `<head>`.
const { seoDisplayName } = await useChampionSeoName(championId, selectedPatch, displayName)

const { breadcrumbItems } = usePlayerChampionPageSeo({ championId, seoDisplayName, playerLabel, profilePath })

const isRefetching = computed(() =>
  isLoadingStatus(championStatus.value)
  || isLoadingStatus(staticStatus.value)
  || isLoadingStatus(staticListStatus.value)
  || isLoadingStatus(runeTreeStatus.value)
  || isLoadingStatus(itemsStatus.value)
  || isLoadingStatus(summonersStatus.value),
)

// The profile's banner, on this champion's splash: the same identity line (the
// truemain it names is the account's, whichever champion the page is on).
const latestPatch = computed(() => versions.value?.[0] ?? null)
const { truemainChampion, identity: heroIdentity } = useTruemainBanner(profile, staticList, playerLabel)

// Frozen prop bundle for the lazy (hydrate-on-visible) matchup spread: `staticList`
// is client-only (`server: false`), so it stays out of the SSR markup until the
// child actually mounts (#834/#837).
const matchupsSnapshot = useLazyHydrationSnapshot(
  { champions: [] as ChampionStaticListItem[] },
  () => ({ champions: staticList.value ?? [] }),
)

// This player's games on the champion: the list under the build and the
// banner's form tiles read the same page, as on the profile. Its lane filter is
// the list's own, independent of the build's position filter.
const matchesPage = ref(1)
const matchesPosition = ref<ChampionPosition | null>(null)
watch(championId, () => { matchesPage.value = 1 })
const {
  matches,
  total: matchesTotal,
  pageSize: matchesPageSize,
  isInitialLoading: matchesLoading,
  notFound: matchesNotFound,
} = useTruemainMatches(nameTag, matchesPage, { championId, position: matchesPosition })

// An empty slice keeps its filters usable, so the reader can undo the one that
// emptied it (see usePlayerChampionEmptySlice).
const { activeFilterLabels, pickerPatch, clearFilters } = usePlayerChampionEmptySlice({
  championId,
  filters,
  setFilter,
  selectedPatch,
})

// A client-side navigation keeps the outgoing page under the loading bar until
// the API has answered, so this page opens on its data (#1689).
await Promise.all([championReady, profileReady, rankHistoryReady, indexingReady])
</script>

<template>
  <div class="mx-auto w-full max-w-7xl p-4 md:p-6">
    <!-- The profile's page (`PagePlayerProfile`), scoped to one champion: the
         player's banner on this champion's splash with the form tiles read
         from their games on it, the champion's build and those games in the
         main column, and the player's cards beside them. -->
    <PagePlayerProfile
      :identity="heroIdentity"
      :patch="latestPatch"
      :level="profile?.identity.summonerLevel ?? null"
      :ranked="profile?.ranked ?? null"
      :truemain="truemainChampion"
      :persona="profile?.persona ?? null"
      :splash-url="splashUrl"
      splash-position="60% 20%"
      :matches="matches"
      :loading="matchesLoading"
    >
      <!-- Truemains > {player} > {champion}, and the build filters on the same
           row — in the degraded (no-build) state too: the pickers are how the
           reader got here and the only way back out. -->
      <template #header>
        <div class="relative flex flex-wrap items-center gap-3">
          <UBreadcrumb :items="breadcrumbItems" />
          <ChampionFilters
            v-if="!championError"
            class="ml-auto"
            :selected-patch="pickerPatch"
            :selected-position="isAllPositions ? null : selectedPosition"
            :patch-options="patchOptions"
            all-positions
            @update:patch="value => setFilter({ patch: value })"
            @update:position="value => setFilter({ position: value ?? ALL_POSITIONS })"
          />
          <UProgress
            v-if="isRefetching"
            size="xs"
            color="primary"
            aria-label="Loading champion"
            class="absolute inset-x-0 -bottom-2.5"
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

      <!-- The champion column, resilient by design: with no aggregate for the
           player (`notEnoughData`) a soft notice replaces the build, and their
           games on the champion — a separate source — still render below. The
           build tabs skeleton until champion + statics land, so the games are
           never shoved down when the client fetch completes (#834). -->
      <template #history>
        <FetchErrorAlert
          v-if="championError"
          :error="championError"
          title="Failed to load this champion"
        />

        <div
          v-else
          class="flex min-w-0 flex-col gap-6"
        >
          <ChampionPlayerBuild
            :champion-id="championId"
            :champion="champion"
            :stale-error="championStaleError"
            :not-enough-data="notEnoughData"
            :champion-name="seoDisplayName"
            :display-name="displayName"
            :display-icon-url="displayIconUrl"
            :position="slicePosition"
            :player-label="playerLabel"
            :global-build-path="pathFor(championId)"
            :filter-labels="activeFilterLabels"
            :static-data="staticData"
            :items-map="itemsMap"
            :summoners-map="summonersMap"
            :summoners-pending="isLoadingStatus(summonersStatus)"
            :rune-tree="runeTree"
            @retry="refreshChampion()"
            @clear="clearFilters"
          />

          <!-- The player's own lanes on one axis (#2094's spread), in the main column
               like the champion page's: the whole field at once needs its width.
               Cross-patch on purpose — one player meets an opponent a handful of times
               in total — and `:champions` frozen until it mounts (#834/#837). -->
          <LazyChampionMatchupSpread
            hydrate-on-visible
            :champion-id="championId"
            :position="slicePosition"
            :name-tag="nameTag"
            v-bind="matchupsSnapshot.value"
            @vue:mounted="matchupsSnapshot.reveal"
          />

          <ChampionPlayerMatchHistory
            v-model:page="matchesPage"
            v-model:position="matchesPosition"
            :name-tag="nameTag"
            :champion-name="displayName"
            :matches="matches"
            :total="matchesTotal"
            :page-size="matchesPageSize"
            :loading="matchesLoading"
            :not-found="matchesNotFound"
            :champions="staticList"
            :items="itemsMap"
            :summoner-spells="summonersMap"
            :rune-tree="runeTree"
          />
        </div>
      </template>

      <!-- The player's cards: the ranked card the profile opens its column on
           (it renders even when the champion aggregate failed — it never
           depended on it) and their performance on the champion. -->
      <template #aside>
        <ChampionPlayerAside
          :ranked="profile?.ranked"
          :profile-loaded="!!profile"
          :history="rankHistory?.entries ?? []"
          :history-loading="rankHistoryLoading"
          :platform-id="profile?.identity.platformId ?? null"
          :champion-failed="!!championError"
          :name-tag="nameTag"
          :champion-id="championId"
          :patch="selectedPatch"
          :position="slicePosition"
          :player-name="playerName"
          :champion-name="displayName ?? null"
        />
      </template>
    </PagePlayerProfile>
  </div>
</template>
