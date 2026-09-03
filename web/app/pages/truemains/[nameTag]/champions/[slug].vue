<script setup lang="ts">
import { POSITION_BY_VALUE, type ChampionPosition } from '~/utils/positions'
import { describeFetchError } from '~/utils/errors'
import { isLoadingStatus } from '~/utils/async-data'
import { parseRouteParam } from '~/utils/route-params'
import { ELO_BRACKET_ALL } from '~/utils/elo-brackets'
import { groupMatchesByDay } from '~/utils/match-history'
import type { ChampionStaticListItem } from '~~/shared/types/static-data'

// Player-scoped mirror of pages/champions/[slug].vue. The static-data fetches,
// loading bar and build tabs are intentionally identical so the page looks
// exactly like the global champion page; the ONLY difference is that
// useChampion is given the route's nameTag, which swaps the data source to
// /api/truemains/{nameTag}/champions/{id} (every aggregate scoped to this
// player's games). Keeping the static fetches aligned (same keys) with both
// the global champion page and the profile page keeps Nuxt's patch-keyed
// caches deduped across navigations.
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
  status: championStatus,
  notEnoughData,
} = useChampion(championId, filters, { nameTag })

// A 404 here is the expected "not enough games" empty state (handled below),
// so useChampion never raises it. Anything that does reach championError is a
// real failure — surface it as a toast on top of the inline alert.
useErrorToast(championError, { title: 'Failed to load champion' })

// Identity for the breadcrumb / header fallback. Cheap and client-cached —
// the profile page primes the same request, so this rarely hits the network.
const { data: profile } = useTruemainProfile(nameTag)
const playerLabel = computed(() => {
  const identity = profile.value?.identity
  if (!identity) return nameTag.value
  return identity.tagLine ? `${identity.gameName}#${identity.tagLine}` : identity.gameName
})
// Same identity, without the tag line: used inline in copy ("Faker vs mains"),
// where the full Riot ID would be noise. Falls back to the raw slug while the
// profile fetch is in flight, like `playerLabel`.
const playerName = computed(() => profile.value?.identity?.gameName ?? nameTag.value)
const profilePath = computed(() => `/truemains/${encodeURIComponent(nameTag.value)}`)

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

// SSR-safe champion name for `<head>` — same composable, same cache key as the
// global champion page, so the two pages share the entry. See
// useChampionSeoName for why `displayName` can't serve `<head>`.
const { seoDisplayName } = await useChampionSeoName(championId, selectedPatch, displayName)

// Truemains > {player} > {champion}, mirroring the schema.org breadcrumb below.
// The champion crumb uses the SSR-safe `seoDisplayName` (client-only
// `displayName` is null during SSR) so the server HTML shows the real name
// rather than `Champion {id}`, matching the global champion page.
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

// Patch for the profile icon in the player header.
const latestPatch = computed(() => versions.value?.[0] ?? null)

// Thin-sample caution. The backend renders a build for any number of games
// (down to one) rather than 404-ing, flagging small samples with
// minSampleMet=false. Surface that as a warning icon next to the champion title
// — like the builder's RecommendationPanel — so a build inferred from a handful
// of games reads as a rough personal signal, not an authoritative meta build.
const lowSampleMessage = computed(() => {
  if (!champion.value || champion.value.minSampleMet)
    return null
  const games = champion.value.totalGames
  return `Only ${games} ${games === 1 ? 'game' : 'games'} on record — this build is inferred from a small personal sample, `
    + 'so treat it as a rough signal rather than a reliable recommendation.'
})

// ─── Empty slice ───────────────────────────────────────────────────────────
// The build filters keep working when the slice they select is empty. Picking a
// lane the player never played 404s the aggregate (`notEnoughData`), and the
// header used to be hidden along with the build — which took the pickers off
// the page at the one moment the reader needs them, leaving "back to the global
// build" as the only way out of a filter they set themselves. The header now
// renders in that state too, over a zero-game slice.

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

// Whether the empty state is the reader's own doing (a filter they set) or the
// champion's (we hold no aggregate at all). The two need different exits: one
// clears the filter, the other can only leave for the global build.
const emptiedByFilters = computed(() => notEnoughData.value && activeFilterLabels.value.length > 0)

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

// ─── Match history ─────────────────────────────────────────────────────────
// This player's recent games on THIS champion. The champion is fixed to the
// page; the lane filter is its OWN control, independent of the build's position
// filter, so you can browse games on any lane without re-scoping the build.
const matchesPage = ref(1)
const matchPosition = ref<ChampionPosition | null>(null)
const {
  matches,
  total: matchesTotal,
  pageSize: matchesPageSize,
  isInitialLoading: matchesInitialLoading,
  notFound: matchesNotFound,
} = useTruemainMatches(nameTag, matchesPage, {
  championId,
  position: matchPosition,
})
function setMatchesPage(next: number) {
  matchesPage.value = Math.max(1, Math.floor(next))
}
function setMatchPosition(next: ChampionPosition | null) {
  matchPosition.value = next
  matchesPage.value = 1
}

// Same dated day-runs as the profile history.
const matchDays = computed(() => groupMatchesByDay(matches.value))

const staticBundleReady = computed(() =>
  Boolean(staticList.value && itemsMap.value && summonersMap.value && runeTree.value),
)

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
  {
    playerName: nameTag.value,
    championName: null as string | null,
  },
  () => ({
    playerName: playerName.value,
    championName: displayName.value ?? null,
  }),
)
</script>

<template>
  <main class="mx-auto w-full max-w-[96rem] space-y-6 p-4 md:p-6">
    <!-- Truemains > {player} > {champion}, linking back to the leaderboard and
         the player's profile. -->
    <UBreadcrumb :items="breadcrumbItems" />

    <!-- Player identity up top so it's obvious this is the truemain's page,
         not the global champion page. -->
    <ProfileHeaderSkeleton v-if="!profile" />
    <ProfileHeader
      v-else
      :identity="profile.identity"
      :patch="latestPatch"
    />

    <div class="h-0.5">
      <UProgress
        v-if="isRefetching"
        size="xs"
        color="primary"
        aria-label="Loading champion"
      />
    </div>

    <UAlert
      v-if="championError"
      color="error"
      variant="soft"
      title="Failed to load champion"
      :description="describeFetchError(championError)"
    />

    <!--
      Player build page, resilient by design: a champion the profile lists as a
      main must always show *something* on click. The build breakdown renders
      whenever the backend holds any aggregate for the player (however thin);
      when it holds none (`notEnoughData` — no timeline-complete ranked game on
      record) we show a soft notice instead of a dead-end, and either way still
      render the player's recent games on the champion below (a separate data
      source that has the raw matches even when the build aggregate doesn't).
    -->
    <template v-else>
      <!-- Rendered in the degraded (no-build) state too: the pickers are how the
           reader got here and the only way back out, so taking them off the page
           when their slice comes back empty strands them. The header
           then stands over a zero-game slice, which the stat line says plainly
           instead of inventing a 0.0% win rate. -->
      <header class="flex flex-wrap items-center gap-4">
        <!-- `seoDisplayName` (SSR-resolved) rather than the client-only
             `displayName`, and skeletons instead of zeroes until the
             player's aggregate lands — same as the global champion page. An
             empty slice is settled, not pending, so it must not skeleton. -->
        <ChampionHeader
          :champion-name="seoDisplayName"
          :champion-icon-url="displayIconUrl"
          :champion-id="championId"
          :position="champion?.position || selectedPosition || ''"
          :total-games="champion?.totalGames ?? 0"
          :total-wins="champion?.totalWins ?? 0"
          :low-sample-message="lowSampleMessage"
          :loading="!champion && !notEnoughData"
        />
        <ChampionFilters
          :selected-patch="pickerPatch"
          :selected-position="selectedPosition"
          :patch-options="patchOptions"
          @update:patch="value => setFilter({ patch: value })"
          @update:position="value => setFilter({ position: value })"
        />
      </header>

      <!--
        Same two-column layout as the global champion page (#703): the build
        breakdown in the main column, matchups and the performance card in a
        right sidebar from the xl breakpoint. No truemains panel here — the page
        is already scoped to one player. Below xl the sidebar stacks under the
        main column.

        The grid is unconditional, and the recent-games list lives *inside* the
        main column rather than full-width below the grid: match rows are the
        same kind of content as the build breakdown and read badly stretched to
        the full 96rem, and keeping one column width means the degraded
        (no-build) state below doesn't reflow the page. It also reserves its
        space from SSR — the header falls back to the URL filters and the build
        tabs show a dedicated skeleton until real champion + static data land —
        so the recent-games section is never shoved down when the client fetch
        completes (#834: it used to jump ~1700px, wrecking CLS, because the
        whole grid was gated on `champion && staticData`).
      -->
      <div class="grid grid-cols-1 items-start gap-6 xl:grid-cols-[minmax(0,1fr)_minmax(0,26rem)]">
        <div class="min-w-0 space-y-6">
          <!-- Two empty states, one block: a slice the reader emptied with a
               filter, and a champion we hold no aggregate for at all. They read
               differently because they end differently — the first is undone by
               clearing the filter, the second only by leaving for the global
               build. The champion icon is gone from here: the header above now
               carries it in both states, and twice is once too many. -->
          <div
            v-if="notEnoughData"
            class="flex flex-col items-center gap-3 surface rounded-lg px-6 py-8 text-center"
          >
            <div class="space-y-1">
              <p class="text-sm font-medium text-default">
                {{ emptiedByFilters ? 'Nothing on these filters' : 'No personal build breakdown yet' }}
              </p>
              <p class="text-sm text-muted">
                <template v-if="emptiedByFilters">
                  {{ playerLabel }} has no game on record for {{ activeFilterLabels.join(' · ') }}.
                  Pick another lane or patch, or clear the filters to go back to their main slice.
                </template>
                <template v-else>
                  We don't have an aggregated build for {{ playerLabel }} on
                  {{ displayName ?? 'this champion' }} yet. Their recent games are below.
                </template>
              </p>
            </div>
            <div class="flex flex-wrap items-center justify-center gap-4">
              <UButton
                v-if="emptiedByFilters"
                size="sm"
                color="neutral"
                variant="subtle"
                icon="i-lucide-filter-x"
                @click="clearFilters"
              >
                Clear filters
              </UButton>
              <NuxtLink
                :to="pathFor(championId)"
                class="rounded text-sm text-primary transition-colors hover:text-primary/80 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
              >
                See the global build for {{ displayName ?? `champion ${championId}` }}
              </NuxtLink>
            </div>
          </div>

          <template v-else>
            <ChampionBuildTabs
              v-if="champion && staticData"
              :builds="champion.builds"
              :champion-static="staticData"
              :items-map="itemsMap ?? {}"
              :summoners-map="summonersMap ?? {}"
              :summoners-pending="isLoadingStatus(summonersStatus)"
              :rune-tree="runeTree ?? null"
            />
            <!-- These tabs carry no population scope, so the real card has no
                 power-spikes section for the skeleton to reserve. -->
            <ChampionBuildTabsSkeleton
              v-else
              :powerspikes="false"
            />

          </template>

          <!-- This player's recent games on this champion — rendered even when the
               build breakdown above is absent (the degraded state), so clicking a
               main always surfaces their actual games. The champion is fixed; the
               lane filter is its own RolePicker, independent of the build's position
               filter. -->
          <section class="flex min-w-0 flex-col gap-3">
            <div class="flex flex-wrap items-center justify-between gap-2">
              <h2 class="text-xs font-semibold uppercase tracking-wide text-muted">
                Recent {{ displayName ?? '' }} games
              </h2>
              <RolePicker
                :position="matchPosition"
                @update:position="setMatchPosition"
              />
            </div>

            <!--
              Same ordering as the profile page: the empty / not-found state needs no
              static data, so it must not sit behind staticBundleReady — a failing
              static fetch would pin the skeletons forever.
            -->
            <template v-if="matchesInitialLoading">
              <MatchRowSkeleton v-for="i in 5" :key="`match-skel-${i}`" />
            </template>
            <template v-else-if="matchesNotFound || matches.length === 0">
              <MatchHistoryEmpty :not-found="matchesNotFound" :filtered="matchPosition !== null" />
            </template>
            <template v-else-if="!staticBundleReady">
              <MatchRowSkeleton v-for="i in 5" :key="`match-skel-${i}`" />
            </template>
            <template v-else>
              <!-- Same day grouping as the profile history, so the two lists read
                   identically. -->
              <template v-for="day in matchDays" :key="day.key">
                <MatchDayHeading v-if="day.label" :label="day.label" />
                <LazyMatchRow
                  v-for="match in day.matches"
                  :key="match.matchId"
                  hydrate-on-visible
                  :match="match"
                  :champions="staticList ?? []"
                  :items="itemsMap ?? {}"
                  :summoner-spells="summonersMap ?? {}"
                  :rune-tree="runeTree!"
                  :name-tag="nameTag"
                />
              </template>
              <div
                v-if="matchesTotal > matchesPageSize"
                class="flex justify-center pt-2"
              >
                <UPagination
                  :page="matchesPage"
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

        <aside class="min-w-0 space-y-6">
          <!-- Performance score (#918): the aggregate of the per-match score
               over this player's recent games on the champion. Lives in the
               sidebar next to matchups. Lazy + hydrate-on-visible with the
               client-only display names frozen until it mounts (#834/#837). -->
          <LazyChampionPlayerPerformance
            hydrate-on-visible
            :name-tag="nameTag"
            :champion-id="championId"
            :patch="selectedPatch"
            :position="selectedPosition"
            v-bind="performanceSnapshot.value"
            @vue:mounted="performanceSnapshot.reveal"
          />

          <!-- Below-the-fold sidebar: lazy-load so its JS lands in its own
               chunk and only hydrates once scrolled into view (#820).
               `:champions` comes from `matchupsSnapshot` (frozen at its
               SSR-matching empty value until `@vue:mounted` reveals the
               live, already-loaded `staticList`) so the deferred hydration
               doesn't mismatch (#834/#837) — same pattern as the global
               champion page. -->
          <!--
            Deliberately cross-patch, unlike the global panel and unlike the
            build sections above it: this slice is one player's own games, where
            a patch filter would leave nearly every opponent under the 3-game
            per-player floor and empty the panel. The global panel needs the
            patch for the opposite reason — its aggregate outlives the matches
            it was folded from, so unscoped it spans *more* history than the page.
          -->
          <LazyChampionMatchups
            hydrate-on-visible
            :champion-id="championId"
            :position="selectedPosition"
            :name-tag="nameTag"
            v-bind="matchupsSnapshot.value"
            @vue:mounted="matchupsSnapshot.reveal"
          />
        </aside>
      </div>
    </template>
  </main>
</template>
