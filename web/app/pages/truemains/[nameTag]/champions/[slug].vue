<script setup lang="ts">
import { POSITION_BY_VALUE } from '#common/utils/positions'
import { isLoadingStatus } from '#common/utils/async-data'
import { parseRouteParam } from '#common/utils/route-params'
import { ELO_BRACKET_ALL } from '#common/utils/elo-brackets'
import type { ChampionPosition } from '#common/utils/positions'
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

// SSR-safe champion name for `<head>` — same composable, same cache key as the
// global champion page, so the two pages share the entry. See
// useChampionSeoName for why `displayName` can't serve `<head>`.
const { seoDisplayName } = await useChampionSeoName(championId, selectedPatch, displayName)

// Truemains > {player} > {champion}, mirroring the schema.org breadcrumb below.
// The champion crumb uses the SSR-safe `seoDisplayName`, as the global page does.
const breadcrumbItems = computed(() => [
  { label: 'Truemains', to: '/truemains' },
  { label: playerLabel.value, to: profilePath.value },
  { label: seoDisplayName.value ?? `Champion ${championId.value}` },
])

useSeoMeta({
  title: () => `${seoDisplayName.value ?? `Champion ${championId.value}`} Build by ${playerLabel.value}`,
  description: () => `${playerLabel.value}'s ${seoDisplayName.value ?? `champion ${championId.value}`} build: `
    + `runes, items and skill order from their real ranked games as a ${seoDisplayName.value ?? 'this champion'} OTP.`,
})

useSchemaOrg([
  defineWebPage({
    name: () => `${seoDisplayName.value ?? 'Champion'} Build by ${playerLabel.value}`,
  }),
  defineBreadcrumb({
    itemListElement: [
      { name: 'Truemains', item: '/truemains' },
      { name: playerLabel.value, item: profilePath.value },
      { name: () => seoDisplayName.value ?? `Champion ${championId.value}` },
    ],
  }),
])

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

// Thin-sample caution: the backend builds from any number of games, flagging
// small samples with minSampleMet=false. Surfaced as a warning icon by the title
// so a handful of games reads as a rough personal signal, not a meta build.
const lowSampleMessage = computed(() => {
  if (!champion.value || champion.value.minSampleMet)
    return null
  const games = champion.value.totalGames
  return `Only ${games} ${games === 1 ? 'game' : 'games'} on record — this build is inferred from a small personal sample, `
    + 'so treat it as a rough signal rather than a reliable recommendation.'
})

// ─── Empty slice ───────────────────────────────────────────────────────────
// The build filters keep working when the slice they select is empty: a lane the
// player never played 404s the aggregate (`notEnoughData`), and the header — its
// pickers included — still renders over the zero-game slice, so the reader can
// undo a filter they set in place.

// Human-readable list of the filters currently narrowing the page, so the empty
// notice can say what emptied it. Only the ones this page actually renders — it
// has no rank or population control.
const activeFilterLabels = computed(() => {
  const labels: string[] = []
  if (filters.value.patch) labels.push(filters.value.patch)
  const position = filters.value.position
  if (position) labels.push(POSITION_BY_VALUE.get(position)?.label ?? position)
  return labels
})

// The patch the API last resolved for this champion. `selectedPatch` falls back
// to the loaded aggregate's patch, which goes null the moment a filter empties
// the slice — blanking the patch select right when it has to be usable. Reset
// on champion change so nothing leaks across a navigation on this same route.
const lastResolvedPatch = ref('')
watch(championId, () => { lastResolvedPatch.value = '' })
watch(selectedPatch, (patch) => { if (patch) lastResolvedPatch.value = patch }, { immediate: true })
const pickerPatch = computed(() => selectedPatch.value || lastResolvedPatch.value)

// The position picker hides its "all positions" button (the API always answers
// for one lane), so clearing is the only way back from an empty lane.
function clearFilters() {
  void setFilter({ patch: null, position: null })
}

// Frozen prop bundle for the lazy (hydrate-on-visible) matchups sidebar — see
// useLazyHydrationSnapshot / the identical pattern on pages/champions/[slug].vue
// for why: `staticList` is client-only (`server: false`), so freezing it
// until the child actually mounts avoids a hydration mismatch (#834/#837).
const matchupsSnapshot = useLazyHydrationSnapshot(
  { champions: [] as ChampionStaticListItem[] },
  () => ({ champions: staticList.value ?? [] }),
)

// And for the performance card (#918), whose only client-only inputs are the
// two display names it prints. `patch`/`position` stay live — they feed the
// fetch key, never the SSR markup, so they can't desynchronise hydration.
const performanceSnapshot = useLazyHydrationSnapshot(
  { playerName: nameTag.value, championName: null as string | null },
  () => ({ playerName: playerName.value, championName: displayName.value ?? null }),
)

// A client-side navigation keeps the outgoing page under the loading bar until
// the API has answered, so this page opens on its data (#1689).
await Promise.all([championReady, profileReady, rankHistoryReady])
</script>

<template>
  <div class="mx-auto w-full max-w-7xl p-4 md:p-6">
    <!-- The profile's page (`PagePlayerProfile`), scoped to one champion: the
         player's banner on this champion's splash with the form tiles read
         from their games on it, the champion's build and those games in the
         main column, and the player's cards beside them — 21rem rather than the
         profile's 18, so the matchups table keeps its champion names. -->
    <PagePlayerProfile
      :identity="heroIdentity"
      :patch="latestPatch"
      :level="profile?.identity.summonerLevel ?? null"
      :ranked="profile?.ranked ?? null"
      :truemain="truemainChampion"
      :persona="profile?.persona ?? null"
      :splash-url="splashUrl"
      splash-position="60% 20%"
      aside-width="21rem"
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
            :selected-position="selectedPosition"
            :patch-options="patchOptions"
            @update:patch="value => setFilter({ patch: value })"
            @update:position="value => setFilter({ position: value })"
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
          <!-- The SSR-resolved name; skeletons until the aggregate lands, but
               an empty slice is settled and says so. -->
          <ChampionPlayerHeading
            :champion-name="seoDisplayName"
            :champion-icon-url="displayIconUrl"
            :champion-id="championId"
            :position="champion?.position || selectedPosition || ''"
            :total-games="champion?.totalGames ?? 0"
            :total-wins="champion?.totalWins ?? null"
            :low-sample-message="lowSampleMessage"
            :loading="!champion && !notEnoughData"
          />

          <StaleContentNotice
            :error="championStaleError"
            subject="the previous slice"
            :on-retry="() => refreshChampion()"
          />

          <ChampionPlayerBuildEmpty
            v-if="notEnoughData"
            :player-label="playerLabel"
            :champion-name="displayName"
            :global-build-path="pathFor(championId)"
            :filter-labels="activeFilterLabels"
            @clear="clearFilters"
          />
          <ChampionBuildTabs
            v-else-if="champion && staticData"
            :builds="champion.builds"
            :champion-static="staticData"
            :items-map="itemsMap ?? {}"
            :summoners-map="summonersMap ?? {}"
            :summoners-pending="isLoadingStatus(summonersStatus)"
            :rune-tree="runeTree ?? null"
          />
          <ChampionBuildTabsSkeleton v-else />

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
           depended on it), their performance on the champion, its matchups. -->
      <template #aside>
        <ProfileRankedCardSkeleton v-if="!profile" />
        <ProfileRankedCard
          v-else
          :ranked="profile.ranked"
          :history="rankHistory?.entries ?? []"
          :history-loading="rankHistoryLoading"
        />

        <!-- Performance score (#918), lazy + hydrate-on-visible with the
             client-only display names frozen until it mounts (#834/#837). -->
        <LazyChampionPlayerPerformance
          v-if="!championError"
          hydrate-on-visible
          :name-tag="nameTag"
          :champion-id="championId"
          :patch="selectedPatch"
          :position="selectedPosition"
          v-bind="performanceSnapshot.value"
          @vue:mounted="performanceSnapshot.reveal"
        />

        <!-- Lazy + hydrate-on-visible (#820), `:champions` frozen (#834/#837).
             Cross-patch on purpose: one player's games under a patch filter
             would leave nearly every opponent under the 3-game floor. -->
        <LazyChampionMatchups
          v-if="!championError"
          hydrate-on-visible
          :champion-id="championId"
          :position="selectedPosition"
          :name-tag="nameTag"
          v-bind="matchupsSnapshot.value"
          @vue:mounted="matchupsSnapshot.reveal"
        />
      </template>
    </PagePlayerProfile>
  </div>
</template>
