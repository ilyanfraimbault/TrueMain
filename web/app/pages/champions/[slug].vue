<script setup lang="ts">
import { POSITION_BY_VALUE } from '#common/utils/positions'
import { isLoadingStatus } from '#common/utils/async-data'

// Layout plus wiring: the fetching and derivation live in composables
// (`useChampionBuildSummary`, `useChampionPageSeo`, `useChampionSliceControls`,
// `useChampionPanelSnapshots`), each documenting the constraints it carries.

const route = useRoute()

// Champion slugs (#1124). The map is app-wide state filled before the first
// render, so this resolves synchronously on the server, at hydration and on
// every client-side navigation — `championId` is a plain computed and every
// fetch below it keeps working exactly as it did under the numeric route.
// The 404 and the legacy-URL 301 live in a middleware, not here: setup does not
// re-run when only the route *param* changes (champion → champion is the same
// component), so a guard in setup would silently stop firing on client-side
// navigation. See `championRouteGuard`.
definePageMeta({
  middleware: to => championRouteGuard(to, segment => `/champions/${segment}`),
})

const { resolveParam } = useChampionSlugs()
const championId = computed(() => resolveParam(String(route.params.slug)).championId ?? Number.NaN)

const { filters, setFilter } = useChampionFilters()

const {
  data: champion,
  error: championError,
  staleError: championStaleError,
  refresh: refreshChampion,
  status: championStatus,
  notEnoughData,
  ready: championReady,
} = useChampion(championId, filters)

// A filter change keeps the previous payload on screen while the new one loads
// (useAsyncData holds `data`), so the build sections silently showed the *old*
// slice — the matchup filter made that obvious. Render the same skeleton as a
// cold load while the champion fetch is in flight. Only the data area: the
// header keeps its values so the page doesn't jump under the cursor.
const championLoading = computed(() => isLoadingStatus(championStatus.value))

// A failed first load is the inline alert below, never also a toast (#1661); a
// failed filter change keeps the previous slice (#1668). A 404 is no error:
// useChampion turns it into notEnoughData, a dedicated empty state.

// Static-data plumbing shared with the player-scoped champion page: the
// patch-pinned rune tree / items / summoner spells (keys shared with /champions
// so the patch-keyed maps stay deduped), the display name/icon fallbacks and
// the patch/position selector state. `selectedPatch` binds to the API-returned
// patch once available so the picker reflects what's actually shown — covers
// useChampion's 404 fallback, which drops the URL filter. On later patch swaps
// it keeps the old patch until the refetch resolves (or while a failed one
// leaves the previous slice up) — intentional, identical to selectedPosition.
const {
  staticData,
  versions,
  staticList,
  runeTree,
  itemsMap,
  summonersMap,
  summonersStatus,
  displayName,
  displayIconUrl,
  patchOptions,
  selectedPatch,
  selectedPosition,
} = useChampionDetailStatics(championId, champion, filters, {
  championSettled: () => !championLoading.value,
})

// Full ddragon version for the truemains sidebar's profile-icon URLs — the
// short activePatch ("15.13") isn't a ddragon CDN path segment.
const latestVersion = computed(() => versions.value?.[0] ?? null)

// Winrate/pickrate trend across the last five patches (#89, #112). Follows the
// resolved lane but is deliberately cross-patch: only the position is sent,
// never the pinned patch. Gated on the champion fetch so it fires once with the
// resolved lane instead of twice (null lane first, then the real one).
const trendReady = computed(() => champion.value !== null)
const trendPosition = computed(() => champion.value?.position || filters.value.position || null)
// `pending` rather than `status`: while the gate is shut the composable
// resolves an empty series to `success`, so `status` alone would flash the
// chart's no-data state for the whole champion fetch.
const { data: championTrend, pending: trendPending } = useChampionTrend(championId, trendPosition, trendReady)

// SSR-safe champion name for `<head>` — see useChampionSeoName for why this
// page can't use `displayName` there, and why the fetch is awaited on the
// server only. Shared verbatim with the player-scoped champion page.
// The banner's splash, through the image proxy at banner width.
const splashUrl = useChampionSplash(displayIconUrl)

const { seoDisplayName } = await useChampionSeoName(championId, selectedPatch, displayName)
const seoPositionLabel = computed(() => POSITION_BY_VALUE.get(trendPosition.value ?? '')?.label)

// The build in words (#1123), SSR-enabled and awaited with the champion fetch.
const buildSummaryFetch = useChampionBuildSummary(championId, filters)
await Promise.all([buildSummaryFetch, championReady])
const { data: buildSummary } = buildSummaryFetch

const { shareTitle, shareDescription, breadcrumbItems } = useChampionPageSeo({
  championId,
  filters,
  seoDisplayName,
  seoPositionLabel,
})

const {
  selectedEloBracket,
  eloBracketParam,
  opponentOptions,
  onOpponentChange,
  noDataForRank,
  bracketNoticeText,
} = useChampionSliceControls({
  championId,
  champion,
  notEnoughData,
  filters,
  setFilter,
  staticList,
  selectedPosition,
})

// Win rate by game duration (issue #537). Follows the resolved lane like the
// trend chart, but is patch-scoped: the active patch filter narrows the slice.
// Gated on the champion fetch so it fires once with the resolved lane — hence
// `pending` rather than `status`, same reason as the trend chart above.
const tier = useChampionTier({
  championId,
  position: trendPosition,
  patch: selectedPatch,
  eloBracket: eloBracketParam,
  truemainsOnly: () => filters.value.truemainsOnly,
})

const { data: championScaling, pending: scalingPending } = useChampionScaling(
  championId,
  trendPosition,
  selectedPatch,
  trendReady,
  eloBracketParam,
)

// Each section drives its own skeleton off its own async status via the
// shared isLoadingStatus util.
const snapshots = useChampionPanelSnapshots({
  trend: championTrend,
  trendPending,
  scaling: championScaling,
  scalingPending,
  staticList,
  itemsMap,
  latestVersion,
})
</script>

<template>
  <div class="mx-auto w-full max-w-[96rem] space-y-6 p-4 md:p-6">
    <!-- Champions > {champion}, mirroring the schema.org breadcrumb. Shown
         across every state (error / no-data / normal) as the first child. -->
    <div class="flex flex-wrap items-center gap-3">
      <UBreadcrumb :items="breadcrumbItems" />
      <!-- The filters share the breadcrumb's row (the dashboard's header line):
           shown wherever the page has a slice to change. The no-data-for-rank
           state keeps its own rank select below, next to its explanation. -->
      <ChampionFilters
        v-if="!championError && !noDataForRank"
        class="ml-auto"
        :selected-patch="selectedPatch"
        :selected-position="selectedPosition"
        :selected-elo-bracket="selectedEloBracket"
        :patch-options="patchOptions"
        :opponent-options="opponentOptions"
        :selected-opponent-id="filters.opponentChampionId ?? null"
        :truemains-only="filters.truemainsOnly"
        @update:patch="value => setFilter({ patch: value })"
        @update:position="value => setFilter({ position: value })"
        @update:elo-bracket="value => setFilter({ eloBracket: value })"
        @update:truemains-only="value => setFilter({ truemainsOnly: value })"
        @update:opponent-champion-id="onOpponentChange"
      />
    </div>

    <FetchErrorAlert
      v-if="championError"
      :error="championError"
      title="Failed to load this champion"
    />

    <ChampionRankEmpty
      v-else-if="noDataForRank"
      :champion-id="championId"
      :display-name="displayName"
      :display-icon-url="displayIconUrl"
      :elo-bracket="selectedEloBracket"
      @update:elo-bracket="value => setFilter({ eloBracket: value })"
    />

    <!--
      No-data state: the API returned 404 for this champion (and its fallback
      too) — no aggregate yet, a brand-new champion or one nobody played. Not
      the error alert: a 404 is "no data", not a failure to retry. The banner and
      the filter row stay so the user can switch slice instead of a dead end.
    -->
    <template v-else-if="notEnoughData">
      <ChampionHero
        :champion-name="seoDisplayName"
        :champion-icon-url="displayIconUrl"
        :champion-id="championId"
        :splash-url="splashUrl"
        :position="champion?.position || selectedPosition || ''"
        :total-games="0"
        :trend="null"
      />

      <UEmpty
        icon="i-lucide-chart-no-axes-column"
        description="Not enough data"
      />
    </template>

    <!--
      Everything below renders immediately — no gate on `champion`/`staticData`.
      The banner and filters fall back to the URL filters and the static list;
      the charts skeleton themselves. Only the build tabs wait for real data.
    -->
    <template v-else>
      <!-- `seoDisplayName` is resolved at SSR, so the h1 carries the real
           champion name in the server HTML; the title only skeletons on a
           client-side navigation, where nothing is known yet. -->
      <ChampionHero
        :champion-name="seoDisplayName"
        :champion-icon-url="displayIconUrl"
        :champion-id="championId"
        :splash-url="splashUrl"
        :position="champion?.position || selectedPosition || ''"
        :patch="selectedPatch"
        :total-games="champion?.totalGames ?? 0"
        :low-sample-message="bracketNoticeText"
        :truemains-only="filters.truemainsOnly"
        :tier="tier"
        :trend="championTrend?.points ?? null"
        :trend-loading="trendPending"
        :loading="!champion"
      >
        <!-- Share controls (#926) ride the banner; the degraded states above
             have nothing worth putting in someone's Discord. -->
        <template #actions>
          <ShareButtons
            :title="shareTitle"
            :description="shareDescription"
          />
        </template>
      </ChampionHero>

      <!-- A failed filter change: the previous slice stays, marked stale (#1668). -->
      <StaleContentNotice
        :error="championStaleError"
        subject="the previous slice"
        :on-retry="() => refreshChampion()"
      />

      <!--
        Two-column layout on wide screens: builds + charts on the left, the
        champion's truemains + matchups in a right sidebar. Below xl the
        sidebar stacks under the main column.
      -->
      <div class="grid grid-cols-1 items-start gap-6 xl:grid-cols-[minmax(0,1fr)_minmax(0,26rem)]">
        <ChampionBuildColumn
          :champion="champion"
          :static-data="staticData"
          :items-map="itemsMap"
          :summoners-map="summonersMap"
          :summoners-pending="isLoadingStatus(summonersStatus)"
          :rune-tree="runeTree"
          :champion-id="championId"
          :position="trendPosition"
          :selected-position="selectedPosition"
          :patch="selectedPatch || null"
          :elo-bracket="eloBracketParam"
          :opponent-champion-id="filters.opponentChampionId ?? null"
          :loading="championLoading"
          :snapshots="snapshots"
        />

        <ChampionSideRail
          :champion-id="championId"
          :position="selectedPosition"
          :patch="selectedPatch"
          :elo-bracket="eloBracketParam"
          :summary="buildSummary"
          :static-data="staticData"
          :items-map="itemsMap"
          :summoners-map="summonersMap"
          :rune-tree="runeTree"
          :snapshots="snapshots"
        />
      </div>
    </template>
  </div>
</template>

