<script setup lang="ts">
import type {
  RuneTreeResponse,
  StaticItemData,
  StaticSummonerSpellData,
} from '~~/shared/types/static-data'
import { isChampionPosition, type ChampionPosition } from '~/utils/positions'
import { ELO_BRACKET_ALL, eloBracketLabel, normalizeEloBracket } from '~/utils/elo-brackets'
import { describeFetchError } from '~/utils/errors'

const route = useRoute()
const championId = computed(() => Number.parseInt(String(route.params.id), 10))

const { filters, setFilter } = useChampionFilters()
const nuxtApp = useNuxtApp()

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

const activePatch = computed(() => champion.value?.patch || filters.value.patch || null)

const { data: staticData } = useChampionStatic(championId, activePatch)
const { data: versions } = useDDragonVersions()

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

// Share keys with /champions so the patch-keyed maps stay deduped across the
// list→detail→list round-trip. The list page issues these same fetches with
// the same keys; without that alignment Nuxt would re-resolve them on mount.
// Each fetch wraps the network call so `markStaticFetched` runs after success
// and `getCachedData` reuses entries across navigations within
// `STATIC_CACHE_TTL_MS` (see static-cache.ts).
const { data: staticList } = useChampionStaticList()
// Pin rune-tree to the champion's active patch so the icon URLs we render
// hit CommunityDragon's per-patch (year-cacheable) tree, and so cached
// payloads don't bleed across patches when the user navigates between them.
const { data: runeTree } = useLazyAsyncData<RuneTreeResponse>(
  () => `rune-tree-${activePatch.value || 'latest'}`,
  async () => {
    const key = `rune-tree-${activePatch.value || 'latest'}`
    const data = await $fetch<RuneTreeResponse>('/api/static/rune-tree', {
      query: activePatch.value ? { patch: activePatch.value } : {},
    })
    markStaticFetched(key, nuxtApp)
    return data
  },
  {
    watch: [activePatch],
    getCachedData: key => getStaticCachedData(key, nuxtApp),
    server: false,
  },
)
const { data: itemsMap } = useLazyAsyncData<Record<number, StaticItemData>>(
  () => `static-items-${activePatch.value || 'latest'}`,
  async () => {
    const key = `static-items-${activePatch.value || 'latest'}`
    const data = await $fetch<Record<number, StaticItemData>>('/api/static/items', {
      query: activePatch.value ? { patch: activePatch.value } : {},
    })
    markStaticFetched(key, nuxtApp)
    return data
  },
  {
    watch: [activePatch],
    getCachedData: key => getStaticCachedData(key, nuxtApp),
    server: false,
  },
)
const { data: summonersMap } = useLazyAsyncData<Record<number, StaticSummonerSpellData>>(
  () => `static-summoners-${activePatch.value || 'latest'}`,
  async () => {
    const key = `static-summoners-${activePatch.value || 'latest'}`
    const data = await $fetch<Record<number, StaticSummonerSpellData>>('/api/static/summoner-spells', {
      query: activePatch.value ? { patch: activePatch.value } : {},
    })
    markStaticFetched(key, nuxtApp)
    return data
  },
  {
    watch: [activePatch],
    getCachedData: key => getStaticCachedData(key, nuxtApp),
    server: false,
  },
)

// Fall back to the list-page entry when the per-champion endpoint is still
// pending or the patch failed to resolve — keeps the header readable instead
// of flashing the numeric id.
const championListEntry = computed(() =>
  (staticList.value ?? []).find(item => item.championId === championId.value) ?? null,
)
const displayName = computed(() =>
  staticData.value?.championName || championListEntry.value?.name || null,
)
const displayIconUrl = computed(() =>
  staticData.value?.championIconUrl || championListEntry.value?.iconUrl || null,
)

useSeoMeta({
  title: () => displayName.value ?? 'TrueMain',
  description: () => `Champion ${championId.value} builds, runes and skill order.`,
})

const patchOptions = computed(() => {
  const seen = new Set<string>(
    (versions.value ?? [])
      .map(p => p.split('.').slice(0, 2).join('.'))
      .filter(Boolean)
      .slice(0, 12),
  )
  if (champion.value?.patch) seen.add(champion.value.patch)
  if (filters.value.patch) seen.add(filters.value.patch)
  return [...seen]
    .map(p => ({ label: p, value: p }))
    .sort((a, b) => b.value.localeCompare(a.value, undefined, { numeric: true }))
})

// Bind to the API-returned patch once available so the picker reflects what's
// actually being shown — covers the 404 fallback in useChampion where the URL
// filter is dropped (no data for the champion on that patch) and the API
// returns its default patch. Mirrors selectedPosition so a no-data patch
// snaps the selector back to the loaded patch instead of leaving the dead
// filter pinned. The URL-filter fallback only applies on the initial load
// (champion.value still null); on later patch swaps champion.value holds the
// previous (stale) data, so the selector keeps showing the old patch until the
// refetch resolves — intentional, and identical to selectedPosition.
const selectedPatch = computed(() => champion.value?.patch || filters.value.patch || '')
// Bind to the API-returned position once available so the picker reflects
// what's actually being shown — covers the 404 fallback in useChampion
// where the URL filter is dropped and the API returns the default position.
// Fall back to the URL filter for the optimistic render before the fetch resolves.
const selectedPosition = computed<ChampionPosition | null>(() => {
  const value = champion.value?.position || filters.value.position || ''
  return isChampionPosition(value) ? value : null
})
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

// Each section drives its own skeleton off its own async status; `idle` is
// the pre-fetch state from useLazy* before the client kicks it off — treat it
// as loading too.
const isLoadingStatus = (s: 'idle' | 'pending' | 'success' | 'error') => s === 'idle' || s === 'pending'
</script>

<template>
  <main class="mx-auto w-full max-w-[96rem] space-y-6 p-4 md:p-6">
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
      above: a 404 is "no data", not a transient failure to retry.
    -->
    <div
      v-else-if="notEnoughData"
      class="flex flex-col items-center gap-3 glass rounded-lg px-6 py-12 text-center"
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
          No data yet for {{ displayName ?? 'this champion' }}
        </p>
        <p class="text-sm text-muted">
          We don't have any games on {{ displayName ?? 'this champion' }} yet — once it's
          been played enough, its builds, runes and stats will show up here.
        </p>
      </div>
    </div>

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

          <ChampionTrendChart
            :points="championTrend?.points ?? []"
            :loading="isLoadingStatus(trendStatus)"
          />

          <ChampionPatchDiff
            :diff="championPatchDiff ?? null"
            :items-map="itemsMap ?? {}"
            :rune-tree="runeTree ?? null"
            :champion-static="staticData"
            :patch-options="patchDiffOptions"
            :from-patch="patchDiffFrom"
            :to-patch="patchDiffTo"
            :loading="isLoadingStatus(patchDiffStatus)"
            @update:from-patch="value => { patchDiffFrom = value }"
            @update:to-patch="value => { patchDiffTo = value }"
          />

          <div class="grid grid-cols-1 gap-6 lg:grid-cols-2">
            <ChampionTimelineLeadsChart
              :intervals="championLeads?.intervals ?? []"
              :loading="isLoadingStatus(leadsStatus)"
            />

            <ChampionScalingChart
              :buckets="championScaling?.buckets ?? []"
              :scaling-index="championScaling?.scalingIndex ?? null"
              :loading="isLoadingStatus(scalingStatus)"
            />
          </div>

          <ChampionPowerspikesChart
            :events="championPowerspikes?.events ?? []"
            :items-map="itemsMap ?? {}"
            :loading="isLoadingStatus(powerspikesStatus)"
          />

          <ChampionRoam
            v-if="trendPosition !== 'JUNGLE'"
            :kp5="championRoam?.roamKp5 ?? null"
            :kp10="championRoam?.roamKp10 ?? null"
            :kp15="championRoam?.roamKp15 ?? null"
            :games="championRoam?.games ?? 0"
            :loading="isLoadingStatus(roamStatus)"
          />
        </div>

        <aside class="min-w-0 space-y-6">
          <ChampionTruemains
            :champion-id="championId"
            :champions="staticList ?? []"
            :rune-tree="runeTree ?? null"
            :items-map="itemsMap ?? {}"
            :patch="latestVersion"
          />

          <ChampionMatchups
            :champion-id="championId"
            :position="selectedPosition"
            :champions="staticList ?? []"
            :elo-bracket="eloBracketParam"
          />
        </aside>
      </div>
    </template>
  </main>
</template>
