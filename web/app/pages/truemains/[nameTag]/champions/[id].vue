<script setup lang="ts">
import type { ChampionPosition } from '~/utils/positions'
import { describeFetchError } from '~/utils/errors'
import { isLoadingStatus } from '~/utils/async-data'
import { parseRouteParam } from '~/utils/route-params'

// Player-scoped mirror of pages/champions/[id].vue. The static-data fetches,
// loading bar and build tabs are intentionally identical so the page looks
// exactly like the global champion page; the ONLY difference is that
// useChampion is given the route's nameTag, which swaps the data source to
// /api/truemains/{nameTag}/champions/{id} (every aggregate scoped to this
// player's games). Keeping the static fetches aligned (same keys) with both
// the global champion page and the profile page keeps Nuxt's patch-keyed
// caches deduped across navigations.
const route = useRoute()

const championId = computed(() => Number.parseInt(String(route.params.id), 10))
const nameTag = computed(() => parseRouteParam(route.params.nameTag))

const { filters, setFilter } = useChampionFilters()

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
} = useChampionDetailStatics(championId, champion, filters, { preferFilterPatch: true })

// Meta-only fetch: see the identical comment on pages/champions/[id].vue —
// `displayName` is sourced from client-only statics, so it's always null
// during SSR. Hits the same 1h-cached endpoint, so it's a cache hit. Only
// awaited server-side, same reasoning as the global champion page.
const seoStaticFetch = useFetch(
  () => `/api/static/${championId.value}`,
  { key: () => `champion-seo-name-${championId.value}-${selectedPatch.value || 'none'}`, query: { patch: selectedPatch.value || undefined } },
)
if (import.meta.server) await seoStaticFetch
const { data: seoStatic } = seoStaticFetch
const seoDisplayName = computed(() => seoStatic.value?.championName ?? displayName.value)

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

const staticBundleReady = computed(() =>
  Boolean(staticList.value && itemsMap.value && summonersMap.value && runeTree.value),
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
      <div
        v-if="notEnoughData && !isRefetching"
        class="flex flex-col items-center gap-3 glass rounded-lg px-6 py-8 text-center"
      >
        <SkeletonImage
          v-if="displayIconUrl"
          :src="displayIconUrl"
          :alt="displayName ?? ''"
          width="64"
          height="64"
          class="size-16 rounded opacity-80"
        />
        <div class="space-y-1">
          <p class="text-sm font-medium text-default">
            No personal build breakdown yet
          </p>
          <p class="text-sm text-muted">
            We don't have an aggregated build for {{ playerLabel }} on
            {{ displayName ?? 'this champion' }} yet. Their recent games are below.
          </p>
        </div>
        <NuxtLink
          :to="`/champions/${championId}`"
          class="rounded text-sm text-primary transition-colors hover:text-primary/80 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
        >
          See the global build for {{ displayName ?? `champion ${championId}` }}
        </NuxtLink>
      </div>

      <template v-if="champion && staticData">
        <header class="flex flex-wrap items-center gap-4">
          <ChampionHeader
            :champion-name="displayName"
            :champion-icon-url="displayIconUrl"
            :champion-id="championId"
            :position="champion.position"
            :total-games="champion.totalGames"
            :total-wins="champion.totalWins"
            :low-sample-message="lowSampleMessage"
          />
          <ChampionFilters
            :selected-patch="selectedPatch"
            :selected-position="selectedPosition"
            :patch-options="patchOptions"
            @update:patch="value => setFilter({ patch: value })"
            @update:position="value => setFilter({ position: value })"
          />
        </header>

        <!--
          Same two-column layout as the global champion page (#703): the build
          breakdown in the main column, matchups in a right sidebar from the xl
          breakpoint. No truemains panel here — the page is already scoped to one
          player. Below xl the sidebar stacks under the main column. The recent
          games list sits below, full-width, so it renders in the degraded
          (no-build) state too.
        -->
        <div class="grid grid-cols-1 items-start gap-6 xl:grid-cols-[minmax(0,1fr)_minmax(0,26rem)]">
          <div class="min-w-0 space-y-6">
            <ChampionBuildTabs
              :builds="champion.builds"
              :champion-static="staticData"
              :items-map="itemsMap ?? {}"
              :summoners-map="summonersMap ?? {}"
              :rune-tree="runeTree ?? null"
            />
          </div>

          <aside class="min-w-0 space-y-6">
            <ChampionMatchups
              :champion-id="championId"
              :position="selectedPosition"
              :champions="staticList ?? []"
              :name-tag="nameTag"
            />
          </aside>
        </div>
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
          <MatchRow
            v-for="match in matches"
            :key="match.matchId"
            :match="match"
            :champions="staticList ?? []"
            :items="itemsMap ?? {}"
            :summoner-spells="summonersMap ?? {}"
            :rune-tree="runeTree!"
            :name-tag="nameTag"
          />
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
    </template>
  </main>
</template>
