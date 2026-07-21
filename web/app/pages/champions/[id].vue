<script setup lang="ts">
import { POSITION_BY_VALUE, type ChampionPosition } from '~/utils/positions'
import { ELO_BRACKET_ALL, eloBracketLabel, normalizeEloBracket } from '~/utils/elo-brackets'
import { describeFetchError } from '~/utils/errors'
import { isLoadingStatus } from '~/utils/async-data'
import type {
  ChampionPatchDiffResponse,
  ChampionPowerCurvePoint,
  ChampionPowerspikeEvent,
  ChampionScalingBucket,
  ChampionTimelineLeadsInterval,
  ChampionTrendPoint,
} from '~~/shared/types/champions'
import type { ChampionStaticData, ChampionStaticListItem, StaticItemData } from '~~/shared/types/static-data'

const route = useRoute()
const championId = computed(() => Number.parseInt(String(route.params.id), 10))

const { filters, setFilter } = useChampionFilters()

const {
  data: champion,
  error: championError,
  notEnoughData,
} = useChampion(championId, filters)

// Real load failures (429/500/network) surface as a toast as well as the
// inline alert below — both read the same line via describeFetchError. A 404
// (no data for this champion) is not an error: useChampion swallows it into
// notEnoughData and we render a dedicated empty state instead.
useErrorToast(championError, { title: 'Failed to load champion' })

// Static-data plumbing shared with the player-scoped champion page: the
// patch-pinned rune tree / items / summoner spells (keys shared with
// /champions so the patch-keyed maps stay deduped across the
// list→detail→list round-trip), the display name/icon fallbacks and the
// patch/position selector state. `selectedPatch` binds to the API-returned
// patch once available so the picker reflects what's actually being shown —
// covers the 404 fallback in useChampion where the URL filter is dropped (no
// data for the champion on that patch) and the API returns its default
// patch. The URL-filter fallback only applies on the initial load
// (champion.value still null); on later patch swaps champion.value holds the
// previous (stale) data, so the selector keeps showing the old patch until
// the refetch resolves — intentional, and identical to selectedPosition.
const {
  staticData,
  versions,
  staticList,
  runeTree,
  itemsMap,
  summonersMap,
  displayName,
  displayIconUrl,
  patchOptions,
  selectedPatch,
  selectedPosition,
} = useChampionDetailStatics(championId, champion, filters)

// Full ddragon version for the truemains sidebar's profile-icon URLs — the
// short activePatch ("15.13") isn't a ddragon CDN path segment. Mirrors what
// /truemains passes to the same rows.
const latestVersion = computed(() => versions.value?.[0] ?? null)

// Winrate/pickrate trend across the last five patches (issues #89, #112).
// Follows the resolved lane so it tracks whatever slice the page is showing,
// but is deliberately cross-patch: the composable forwards only the position,
// never the pinned patch, so the active patch filter never scopes the chart
// and the series always spans recent history. Gated on the champion fetch so
// it fires once with the resolved lane instead of twice (an initial call with
// a null lane, then a refetch the moment the champion's position lands).
const trendReady = computed(() => champion.value !== null)
const trendPosition = computed(() => champion.value?.position || filters.value.position || null)
const { data: championTrend, status: trendStatus } = useChampionTrend(championId, trendPosition, trendReady)

// Meta-only fetch: `displayName` above is sourced from client-only
// (`server: false`) statics chosen to avoid hydration mismatches on the
// visual build content, which means it's always null during SSR — the exact
// HTML Google indexes would permanently show "Champion {id}" instead of the
// champion name. `<head>` tags aren't part of Vue's DOM diff, so a dedicated
// SSR-enabled fetch here carries no hydration risk. Hits the same
// `defineCachedEventHandler` (1h TTL) as useChampionStatic, so it's a cache
// hit, not an extra DDragon round-trip. Only awaited server-side: the app has
// no NuxtLoadingIndicator/Suspense fallback on <NuxtPage>, so awaiting this on
// the client would freeze the outgoing page with no feedback on every
// client-side champion navigation, purely for a `<head>`-only value.
const seoStaticFetch = useFetch(
  () => `/api/static/${championId.value}`,
  { key: () => `champion-seo-name-${championId.value}-${selectedPatch.value || 'none'}`, query: { patch: selectedPatch.value || undefined } },
)
if (import.meta.server) await seoStaticFetch
const { data: seoStatic } = seoStaticFetch
const seoDisplayName = computed(() => seoStatic.value?.championName ?? displayName.value)
const seoPositionLabel = computed(() => POSITION_BY_VALUE.get(trendPosition.value ?? '')?.label)

useSeoMeta({
  title: () => seoDisplayName.value
    ? `${seoDisplayName.value}${seoPositionLabel.value ? ` ${seoPositionLabel.value}` : ''} Build`
    : `Champion ${championId.value} Build`,
  description: () => seoDisplayName.value
    ? `${seoDisplayName.value} build guide: best runes, items and skill order`
      + `${seoPositionLabel.value ? ` for ${seoPositionLabel.value}` : ''}, based on real ranked games. `
      + `See the top OTP ${seoDisplayName.value} one-tricks on TrueMain.`
    : `Champion builds, runes and skill order from true main players.`,
})

useSchemaOrg([
  defineWebPage({
    name: () => seoDisplayName.value ? `${seoDisplayName.value} Build` : undefined,
    description: () => `${seoDisplayName.value ?? 'Champion'} runes, items and skill order.`,
  }),
  defineBreadcrumb({
    itemListElement: [
      { name: 'Champions', item: '/champions' },
      { name: () => seoDisplayName.value ?? `Champion ${championId.value}` },
    ],
  }),
])

// Visible breadcrumb, mirroring the schema.org hierarchy above. Uses the
// SSR-safe `seoDisplayName` (client-only `displayName` is null during SSR) so
// the crumb renders the champion name in the server HTML, not `Champion {id}`.
const breadcrumbItems = computed(() => [
  { label: 'Champions', to: '/champions' },
  { label: seoDisplayName.value ?? `Champion ${championId.value}` },
])

// Elo filter (issue #526). Bind to the API-returned filter once available so
// the rank select reflects what's actually shown; fall back to the URL filter
// for the optimistic render before the fetch resolves.
const selectedEloBracket = computed<string>(() =>
  normalizeEloBracket(champion.value?.eloBracket || filters.value.eloBracket),
)

// The elo filter forwarded to every live panel (matchups / leads / scaling /
// item-timings / roam). Sourced from the URL filter and undefined for ALL, so
// the query param + cache key stay clean — the same contract patch/position use.
const eloBracketParam = computed(() => filters.value.eloBracket)

// A rank filter with no data: the fetch 404'd on a specific rank (the champion
// may well have builds in other ranks). Distinct from the champion-level "no
// data at all" state below — here we keep the rank select so the user can pick
// another rank instead of hitting a dead end.
const noDataForRank = computed(() =>
  notEnoughData.value && selectedEloBracket.value !== ELO_BRACKET_ALL,
)

// Coverage / min-sample guards: only meaningful for a narrow (non-ALL) band.
// `eloCoverage` is the share of all-rank games this slice covers; `minSampleMet`
// is false for tiny high-elo slices. Surface a notice so a thin Master+ build
// reads as "treat with caution" rather than authoritative.
const showBracketNotice = computed(() =>
  Boolean(champion.value)
  && selectedEloBracket.value !== ELO_BRACKET_ALL
  && (!champion.value!.minSampleMet || champion.value!.eloCoverage < 0.1),
)
const bracketCoveragePercent = computed(() =>
  champion.value ? Math.round(champion.value.eloCoverage * 100) : 0,
)
const bracketNoticeText = computed(() => {
  const label = eloBracketLabel(selectedEloBracket.value)
  const games = champion.value?.totalGames ?? 0
  if (champion.value && !champion.value.minSampleMet) {
    return `Only ${games} ${games === 1 ? 'game' : 'games'} in ${label} (${bracketCoveragePercent.value}% of all ranks) — `
      + 'too few to be reliable. Treat this build as a rough signal.'
  }
  return `${label} covers just ${bracketCoveragePercent.value}% of all-rank games — a narrow slice, so read it with caution.`
})

// Average per-interval lead vs the lane opponent (issue #525). Follows the
// resolved lane like the trend chart, but is patch-scoped: the active patch
// filter narrows the slice. Gated on the champion fetch so it fires once with
// the resolved lane. Empty until matches are (re-)ingested with snapshots.
const { data: championLeads, status: leadsStatus } = useChampionTimelineLeads(
  championId,
  trendPosition,
  selectedPatch,
  trendReady,
  eloBracketParam,
)

// Win rate by game duration (issue #537). Same lane/patch scoping and gating as
// the leads chart; computed from match outcomes, so it has data even before any
// timeline snapshots are ingested.
const { data: championScaling, status: scalingStatus } = useChampionScaling(
  championId,
  trendPosition,
  selectedPatch,
  trendReady,
  eloBracketParam,
)

// Power curve event spikes — completed build items ranked by how much the
// champion's lead accelerates after them (issue #571). Same lane/patch scoping
// and gating; rendered against the page's static item map.
const { data: championPowerspikes, status: powerspikesStatus } = useChampionPowerspikes(
  championId,
  trendPosition,
  selectedPatch,
  trendReady,
  eloBracketParam,
)

// Roam metric — out-of-lane early kill participations (issue #536). Same lane/patch
// scoping and gating as the other timeline-derived stats.
const { data: championRoam, status: roamStatus } = useChampionRoam(
  championId,
  trendPosition,
  selectedPatch,
  trendReady,
  eloBracketParam,
)

// Per-champion patch diff (issue #534): what changed for the champion between
// two patches — win-rate swing, build/rune/skill shifts. Follows the resolved
// lane like the trend chart but is deliberately cross-patch (it picks its own
// two patches), so the active patch filter never scopes it. The two selectors
// hold null until the user picks, letting the backend default to the two most
// recent patches with data; gated on the champion fetch like the other stats.
const patchDiffFrom = ref<string | null>(null)
const patchDiffTo = ref<string | null>(null)
// Reset the manual selection when the champion or lane changes so a patch that
// has no data on the new champion/lane can't linger in the pickers — the backend
// re-defaults. Watching championId too matters when navigating between champions
// that share a dominant lane (e.g. two ADCs on BOTTOM): trendPosition stays put,
// so without it the previous champion's picked patches would silently carry over.
watch([championId, trendPosition], () => {
  patchDiffFrom.value = null
  patchDiffTo.value = null
})
const { data: championPatchDiff, status: patchDiffStatus } = useChampionPatchDiff(
  championId,
  trendPosition,
  patchDiffFrom,
  patchDiffTo,
  trendReady,
)
// The patch-diff selectors draw from the page-wide recent-patch list, but the
// backend resolves the diff against the champion's actual data patches — which
// can be older than the 12 newest ddragon versions for a sparsely-played
// champion. Union the resolved from/to in (newest first) so a selector never
// shows blank for a value that isn't in the recent list.
const patchDiffOptions = computed(() => {
  const seen = new Map(patchOptions.value.map(option => [option.value, option]))
  for (const patch of [championPatchDiff.value?.from?.patch, championPatchDiff.value?.to?.patch]) {
    if (patch && !seen.has(patch)) seen.set(patch, { label: patch, value: patch })
  }
  return [...seen.values()].sort((a, b) => b.value.localeCompare(a.value, undefined, { numeric: true }))
})
// The patch-diff fetch is gated on `trendReady` (the champion fetch, which is
// server:false so null on mount). While the gate is closed the composable
// resolves an empty stub straight to `success`, so we must treat "gate closed"
// as loading too — otherwise the section would flash hidden (a `success` status
// with availablePatchCount 0) for the whole champion fetch before reappearing.
const patchDiffLoading = computed(() =>
  !trendReady.value || isLoadingStatus(patchDiffStatus.value),
)
// Hide the whole section when the champion/lane has fewer than two patches of
// data: a single-patch diff can only compare a patch against itself (flat,
// meaningless). Kept visible while loading so the skeleton stays mounted and
// the layout below never shifts.
const showPatchDiff = computed(() =>
  patchDiffLoading.value
  || (championPatchDiff.value?.availablePatchCount ?? 0) >= 2,
)

// When useChampion's 404 fallback drops the URL filters (no data for the
// champion on that patch/position) the API returns the default slice, but the
// dead patch/position query param lingers in the URL. Once the fetch resolves,
// reconcile the URL with what was actually loaded so a no-data selection snaps
// the address bar back to the initial state instead of pinning a stale filter.
// The watch fires when champion data changes (never on the optimistic
// stale-data phase) and once immediately on mount if champion is already
// populated (e.g. an SSR payload) — so the dead filter is reconciled on the
// first render too, not only on the next change. A *valid* selection — where
// the API echoes the request — never triggers a reset.
watch(champion, (data) => {
  if (!data) return
  // Only reset when the API actually returned a (truthy) value that differs:
  // a missing/empty patch or position in the response means "no slice info",
  // not "your valid filter was dropped", so it must never clear a live filter.
  const updates: { patch?: string | null, position?: ChampionPosition | null } = {}
  if (filters.value.patch && data.patch && filters.value.patch !== data.patch) updates.patch = null
  if (filters.value.position && data.position && filters.value.position !== data.position) updates.position = null
  if (updates.patch !== undefined || updates.position !== undefined) setFilter(updates).catch(console.error)
}, { immediate: true })

// Each section drives its own skeleton off its own async status via the
// shared isLoadingStatus util.

// ─── Lazy-hydration snapshots ───────────────────────────────────────────────
// The charts/panels below are `hydrate-on-visible` (their JS is heavy —
// nuxt-charts — so it's kept out of the initial hydration pass, #820) but
// every value they render comes from client-only (`server: false`)
// composables. SSR always renders their empty/loading state; without
// freezing, a child's *deferred* hydration (on scroll, well after the
// client-only fetches have resolved) would reconcile against that stale SSR
// snapshot using already-loaded data — a hydration mismatch on every one of
// them, forcing Vue to discard and rebuild each subtree exactly as it enters
// the viewport (#834/#837 — that's what caused the reported scroll jank).
// `useLazyHydrationSnapshot` keeps each child's first (hydration) render
// identical to SSR; `@vue:mounted="…Snapshot.reveal"` on the child then swaps
// in the live, reactive value as a normal post-hydration update.
const trendSnapshot = useLazyHydrationSnapshot(
  { points: [] as ChampionTrendPoint[], loading: true },
  () => ({ points: championTrend.value?.points ?? [], loading: isLoadingStatus(trendStatus.value) }),
)
const patchDiffSnapshot = useLazyHydrationSnapshot(
  {
    diff: null as ChampionPatchDiffResponse | null,
    itemsMap: {} as Record<number, StaticItemData>,
    championStatic: null as ChampionStaticData | null,
    patchOptions: [] as Array<{ label: string, value: string }>,
    loading: true,
  },
  () => ({
    diff: championPatchDiff.value ?? null,
    itemsMap: itemsMap.value ?? {},
    championStatic: staticData.value ?? null,
    patchOptions: patchDiffOptions.value,
    loading: patchDiffLoading.value,
  }),
)
const leadsSnapshot = useLazyHydrationSnapshot(
  { intervals: [] as ChampionTimelineLeadsInterval[], loading: true },
  () => ({ intervals: championLeads.value?.intervals ?? [], loading: isLoadingStatus(leadsStatus.value) }),
)
const scalingSnapshot = useLazyHydrationSnapshot(
  { buckets: [] as ChampionScalingBucket[], scalingIndex: null as number | null, loading: true },
  () => ({
    buckets: championScaling.value?.buckets ?? [],
    scalingIndex: championScaling.value?.scalingIndex ?? null,
    loading: isLoadingStatus(scalingStatus.value),
  }),
)
const powerspikesSnapshot = useLazyHydrationSnapshot(
  {
    curve: [] as ChampionPowerCurvePoint[],
    events: [] as ChampionPowerspikeEvent[],
    itemsMap: {} as Record<number, StaticItemData>,
    loading: true,
  },
  () => ({
    curve: championPowerspikes.value?.curve ?? [],
    events: championPowerspikes.value?.events ?? [],
    itemsMap: itemsMap.value ?? {},
    loading: isLoadingStatus(powerspikesStatus.value),
  }),
)
const roamSnapshot = useLazyHydrationSnapshot(
  { kp5: null as number | null, kp10: null as number | null, kp15: null as number | null, games: 0, loading: true },
  () => ({
    kp5: championRoam.value?.roamKp5 ?? null,
    kp10: championRoam.value?.roamKp10 ?? null,
    kp15: championRoam.value?.roamKp15 ?? null,
    games: championRoam.value?.games ?? 0,
    loading: isLoadingStatus(roamStatus.value),
  }),
)
const truemainsSnapshot = useLazyHydrationSnapshot(
  { champions: [] as ChampionStaticListItem[], itemsMap: {} as Record<number, StaticItemData>, patch: null as string | null },
  () => ({ champions: staticList.value ?? [], itemsMap: itemsMap.value ?? {}, patch: latestVersion.value }),
)
const matchupsSnapshot = useLazyHydrationSnapshot(
  { champions: [] as ChampionStaticListItem[] },
  () => ({ champions: staticList.value ?? [] }),
)
</script>

<template>
  <main class="mx-auto w-full max-w-[96rem] space-y-6 p-4 md:p-6">
    <!-- Champions > {champion}, mirroring the schema.org breadcrumb. Shown
         across every state (error / no-data / normal) as the first child. -->
    <UBreadcrumb :items="breadcrumbItems" />

    <UAlert
      v-if="championError"
      color="error"
      variant="soft"
      title="Failed to load champion"
      :description="describeFetchError(championError)"
    />

    <!--
      No-data-for-this-rank state: the picked rank has no games (the champion may
      well have builds in other ranks). We keep the rank select visible so the
      user can switch rank — a dead end otherwise — rather than silently showing
      all-ranks data under the selected rank.
    -->
    <div
      v-else-if="noDataForRank"
      class="space-y-6"
    >
      <header class="flex flex-wrap items-center gap-3">
        <SkeletonImage
          v-if="displayIconUrl"
          :src="displayIconUrl"
          :alt="displayName ?? ''"
          width="48"
          height="48"
          class="size-12 rounded"
        />
        <h1 class="text-lg font-semibold text-default">
          {{ displayName ?? `Champion ${championId}` }}
        </h1>
      </header>

      <ChampionEloFilter
        :model-value="selectedEloBracket"
        @update:model-value="value => setFilter({ eloBracket: value })"
      />

      <div class="flex flex-col items-center gap-1 glass rounded-lg px-6 py-12 text-center">
        <p class="text-sm font-medium text-default">
          No {{ displayName ?? 'champion' }} games in {{ eloBracketLabel(selectedEloBracket) }} yet
        </p>
        <p class="text-sm text-muted">
          Pick another rank above, or
          <button
            type="button"
            class="rounded text-primary transition-colors hover:text-primary/80 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
            @click="setFilter({ eloBracket: null })"
          >
            see all ranks</button>.
        </p>
      </div>
    </div>

    <!--
      No-data empty state: the API returned 404 for this champion (and, if a
      patch/position was pinned, the fallback 404'd too) — we simply don't hold
      any aggregate for them yet (a brand-new champion, or one nobody in the
      dataset has played). This is deliberately distinct from the error alert
      above: a 404 is "no data", not a transient failure to retry. We still
      render the base header (name + patch/position/rank pickers) so the user
      can switch slice from here instead of hitting a dead end, and show a plain
      "Not enough data" notice below.
    -->
    <template v-else-if="notEnoughData">
      <header class="flex flex-wrap items-center gap-4">
        <ChampionHeader
          :champion-name="displayName"
          :champion-icon-url="displayIconUrl"
          :champion-id="championId"
          :position="champion?.position || selectedPosition || ''"
          :total-games="champion?.totalGames ?? 0"
          :total-wins="champion?.totalWins ?? 0"
        />
        <ChampionFilters
          :selected-patch="selectedPatch"
          :selected-position="selectedPosition"
          :selected-elo-bracket="selectedEloBracket"
          :patch-options="patchOptions"
          @update:patch="value => setFilter({ patch: value })"
          @update:position="value => setFilter({ position: value })"
          @update:elo-bracket="value => setFilter({ eloBracket: value })"
        />
      </header>

      <div class="flex flex-col items-center gap-1 glass rounded-lg px-6 py-12 text-center">
        <p class="text-sm text-muted">
          Not enough data
        </p>
      </div>
    </template>

    <!--
      Everything below renders immediately and independently — no gate on
      `champion`/`staticData` resolving. Header/filters already fall back to
      the URL filters and the static champion list; the charts already accept
      a `loading` flag and skeleton themselves. Only the build tabs need real
      champion + static data, so that section alone shows a dedicated skeleton
      until both resolve.
    -->
    <template v-else>
      <header class="flex flex-wrap items-center gap-4">
        <ChampionHeader
          :champion-name="displayName"
          :champion-icon-url="displayIconUrl"
          :champion-id="championId"
          :position="champion?.position || selectedPosition || ''"
          :total-games="champion?.totalGames ?? 0"
          :total-wins="champion?.totalWins ?? 0"
        />
        <ChampionFilters
          :selected-patch="selectedPatch"
          :selected-position="selectedPosition"
          :selected-elo-bracket="selectedEloBracket"
          :patch-options="patchOptions"
          @update:patch="value => setFilter({ patch: value })"
          @update:position="value => setFilter({ position: value })"
          @update:elo-bracket="value => setFilter({ eloBracket: value })"
        />
      </header>

      <UAlert
        v-if="showBracketNotice"
        color="warning"
        variant="soft"
        :title="`Small ${eloBracketLabel(selectedEloBracket)} sample`"
        :description="bracketNoticeText"
        icon="i-lucide-triangle-alert"
      />

      <!--
        Two-column layout on wide screens: builds + charts on the left, the
        champion's truemains + matchups in a right sidebar. Below xl the
        sidebar stacks under the main column.
      -->
      <div class="grid grid-cols-1 items-start gap-6 xl:grid-cols-[minmax(0,1fr)_minmax(0,26rem)]">
        <div class="min-w-0 space-y-6">
          <ChampionBuildTabs
            v-if="champion && staticData"
            :builds="champion.builds"
            :champion-static="staticData"
            :items-map="itemsMap ?? {}"
            :summoners-map="summonersMap ?? {}"
            :rune-tree="runeTree ?? null"
          />
          <ChampionBuildTabsSkeleton v-else />

          <!--
            Everything below the build tabs is below the fold and pulls the
            heavy charting bundle (nuxt-charts). Lazy-load each so its JS lands
            in its own chunk and only downloads/hydrates once scrolled into
            view — keeps the champion detail route's initial JS lean (#820).
            Props come from the `…Snapshot` bundles above (frozen at their
            SSR-matching value until `@vue:mounted` reveals the live data) so
            the deferred hydration doesn't mismatch (#834/#837); `rune-tree`,
            `from-patch` and `to-patch` are bound directly since they're
            SSR-safe/locally-stable and don't need freezing.
          -->
          <LazyChampionTrendChart
            hydrate-on-visible
            v-bind="trendSnapshot.value"
            @vue:mounted="trendSnapshot.reveal"
          />

          <LazyChampionPatchDiff
            v-if="showPatchDiff"
            hydrate-on-visible
            v-bind="patchDiffSnapshot.value"
            :rune-tree="runeTree ?? null"
            :from-patch="patchDiffFrom"
            :to-patch="patchDiffTo"
            @vue:mounted="patchDiffSnapshot.reveal"
            @update:from-patch="value => { patchDiffFrom = value }"
            @update:to-patch="value => { patchDiffTo = value }"
          />

          <div class="grid grid-cols-1 gap-6 lg:grid-cols-2">
            <LazyChampionTimelineLeadsChart
              hydrate-on-visible
              v-bind="leadsSnapshot.value"
              @vue:mounted="leadsSnapshot.reveal"
            />

            <LazyChampionScalingChart
              hydrate-on-visible
              v-bind="scalingSnapshot.value"
              @vue:mounted="scalingSnapshot.reveal"
            />
          </div>

          <LazyChampionPowerspikesChart
            hydrate-on-visible
            v-bind="powerspikesSnapshot.value"
            @vue:mounted="powerspikesSnapshot.reveal"
          />

          <LazyChampionRoam
            v-if="trendPosition !== 'JUNGLE'"
            hydrate-on-visible
            v-bind="roamSnapshot.value"
            @vue:mounted="roamSnapshot.reveal"
          />
        </div>

        <aside class="min-w-0 space-y-6">
          <LazyChampionTruemains
            hydrate-on-visible
            :champion-id="championId"
            :rune-tree="runeTree ?? null"
            v-bind="truemainsSnapshot.value"
            @vue:mounted="truemainsSnapshot.reveal"
          />

          <LazyChampionMatchups
            hydrate-on-visible
            :champion-id="championId"
            :position="selectedPosition"
            :elo-bracket="eloBracketParam"
            v-bind="matchupsSnapshot.value"
            @vue:mounted="matchupsSnapshot.reveal"
          />
        </aside>
      </div>
    </template>
  </main>
</template>
