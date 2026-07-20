<script setup lang="ts">
import type { ChampionSummaryResponse } from '~~/shared/types/champions'
import { POSITION_BY_VALUE, isChampionPosition, type ChampionPosition } from '~/utils/positions'
import { normalizeEloBracket } from '~/utils/elo-brackets'
import { isLoadingStatus } from '~/utils/async-data'
import { formatPercentage } from '~~/shared/utils/ddragon'

// Mirrors the backend default; the page size is fixed in the UI (no
// per-page selector) so the only stateful pagination value carried in the
// URL is the page number.
const PAGE_SIZE = 50

useSeoMeta({
  title: 'Champion Builds',
  description: 'Browse every champion build by lane — most-played runes, items and skill order, winrate and pickrate for the current patch.',
})

useSchemaOrg([
  defineWebPage({ name: 'Champion Builds' }),
])

const router = useRouter()

const { filters, setFilter } = useChampionFilters()

const { currentPage, setPage } = useRoutePage()

// All four fetches are client-only (`server: false`) so SSR ships a
// deterministic empty shell under the skeleton/progress bar instead of
// racing the data into the rendered HTML — without it, fast local API
// responses resolved before the SSR render completed, baking `isPending=false`
// into the server output while the client hydrated with `isPending=true`,
// producing `<!-- -->` vs `<div>` and `<ul>` vs `<div>` hydration mismatches.
//
// The endpoint returns the full directory (~500 rows on a populated patch).
// Pagination is applied client-side below so search + position filters can
// stay client-side too and the user can paginate filtered subsets without
// extra round-trips.
const {
  data: summaries,
  error: summariesError,
  status: summariesStatus,
} = useLazyAsyncData<ChampionSummaryResponse[]>(
  () => `champions-list-${filters.value.patch ?? 'latest'}-${filters.value.eloBracket ?? 'ALL'}`,
  () => {
    const patch = filters.value.patch
    const elo = filters.value.eloBracket
    return $fetch<ChampionSummaryResponse[]>('/api/champions', {
      query: {
        ...(patch ? { patch } : {}),
        // Cumulative "X+" threshold; the composable already omits the default
        // ALL, so a value here is always a real filter the backend expands.
        ...(elo ? { eloBracket: elo } : {}),
      },
    })
  },
  {
    watch: [() => filters.value.patch, () => filters.value.eloBracket],
    server: false,
    default: () => [],
  },
)
// Static fetches use `useLazyAsyncData` (not `useLazyFetch`) so the handler
// closure can call `markStaticFetched` after the network round trip — the
// `useFetch` wrapper hides that hook. `getCachedData` reuses entries across
// navigations within `STATIC_CACHE_TTL_MS` (see static-cache.ts).
const {
  data: staticList,
  error: staticError,
  status: staticStatus,
} = useChampionStaticList()
const { data: versions } = useDDragonVersions()

const apiPatch = computed(() => summaries.value?.[0]?.patchVersion ?? '')
const selectedPatch = computed(() => filters.value.patch || apiPatch.value || '')

// Item icons are patch-specific (new items + visual refreshes ship with a
// patch), so the fetch must follow whichever patch the list is currently
// showing. `immediate: false` + the watcher below defers the first fetch until
// `selectedPatch` is known, so we don't issue a redundant `static-items-latest`
// round-trip and then immediately refetch under the resolved patch key.
const {
  data: itemsMap,
  error: itemsError,
  status: itemsStatus,
  execute: fetchItems,
} = useStaticItems(selectedPatch, { immediate: false, unresolvedKeySegment: 'pending' })
watch(selectedPatch, (patch) => {
  if (patch) void fetchItems()
}, { immediate: true })

// Pin rune-tree to the same patch as the list so the icon URLs we hand to
// IPX hit CommunityDragon's per-patch (year-cacheable) tree instead of
// `latest` (short TTL + moving target). Cache key includes the patch so
// switching the dropdown swaps payloads cleanly.
const {
  data: runeTree,
  error: runeTreeError,
  status: runeTreeStatus,
} = useStaticRuneTree(selectedPatch)

const error = computed(() => summariesError.value ?? staticError.value ?? itemsError.value ?? runeTreeError.value)
// Treat the pre-fetch `'idle'` state from `useLazy*` the same as `'pending'`
// (see isLoadingStatus), otherwise the SSR shell briefly renders the empty
// `<ul>` (and the "No champions match…" copy below) before the client kicks
// off the first fetch. All four sources gate the skeleton so we never show
// rows with placeholder `Champion {id}` names or missing rune / item icons.
const isPending = computed(() =>
  isLoadingStatus(summariesStatus.value)
  || isLoadingStatus(staticStatus.value)
  || isLoadingStatus(runeTreeStatus.value)
  || isLoadingStatus(itemsStatus.value),
)

const patchOptions = usePatchOptions(versions, apiPatch, () => filters.value.patch)

// null = "All positions" — matches the RolePicker contract shared with
// the leaderboard filter strip.
const selectedPosition = computed<ChampionPosition | null>(() => {
  const value = filters.value.position ?? ''
  return isChampionPosition(value) ? value : null
})

// ALL when the `?elo=` param is absent (the composable omits the default), so
// the picker always reflects a valid threshold.
const selectedEloBracket = computed<string>(() => normalizeEloBracket(filters.value.eloBracket))

// Champion filter sources from `?championId=` so deep links and back/forward
// keep the selection. Uses the same ChampionPicker as the truemain
// leaderboard so the UX matches across the two list pages.
const filterChampionId = useRouteQueryChampionId()

// Filter changes go through the shared composable with `resetPage` so any
// change anchors back on page 1 in the same atomic router.replace.
function onPatchChange(value: unknown) {
  if (typeof value !== 'string' || !value) return
  void setFilter({ patch: value }, { resetPage: true })
}

async function selectPosition(value: ChampionPosition | null) {
  await setFilter({ position: value }, { resetPage: true })
}

async function selectChampion(value: number | null) {
  await setFilter({ championId: value }, { resetPage: true })
}

function onEloBracketChange(value: string) {
  void setFilter({ eloBracket: value }, { resetPage: true })
}

const championsById = useChampionsById(staticList)

const baseRows = computed(() =>
  (summaries.value ?? []).map(summary => ({
    ...summary,
    name: championsById.value.get(summary.championId)?.name ?? `Champion ${summary.championId}`,
    iconUrl: championsById.value.get(summary.championId)?.iconUrl ?? '',
  })),
)

const filteredRows = computed(() => {
  let rows = baseRows.value
  const pos = selectedPosition.value
  if (pos !== null) rows = rows.filter(row => row.position === pos)
  const cid = filterChampionId.value
  if (cid !== null) rows = rows.filter(row => row.championId === cid)
  return rows
})

// Client-side pagination: slice the filtered list into pages of PAGE_SIZE.
// `totalCount` follows `filteredRows.length` so the page count adjusts to
// search + position filters without an extra round-trip.
const totalCount = computed<number>(() => filteredRows.value.length)
const pagedRows = computed(() => {
  const start = (currentPage.value - 1) * PAGE_SIZE
  return filteredRows.value.slice(start, start + PAGE_SIZE)
})

// Reset to page 1 when the filtered set shrinks below the current offset,
// either because the user typed in the search box or because a filter
// dropped enough rows to invalidate the current page anchor.
watch(totalCount, (count) => {
  const start = (currentPage.value - 1) * PAGE_SIZE
  if (count > 0 && start >= count) void setPage(1)
})

// Per #147, the row is not a `<NuxtLink>` (and therefore not an `<a>`) because
// the navigation target shows accounts main-ing the champion, and the user
// asked for a button-style click target. We push the route programmatically
// from a `<div role="button">` to keep the existing flex layout (a `<button>`
// would force us to unset user-agent button styling). Keyboard activation is
// wired to Enter and Space so the element behaves like a real button.
function rowDestination(row: { championId: number, position: string }) {
  return {
    path: `/champions/${row.championId}`,
    query: {
      ...(selectedPatch.value ? { patch: selectedPatch.value } : {}),
      ...(row.position ? { position: row.position } : {}),
    },
  }
}

function onRowActivate(row: { championId: number, position: string }) {
  void router.push(rowDestination(row))
}

// Resolve build ids the same way the leaderboard surfaces do. `item` keeps
// its historical `staticItem` name at the template call sites.
const { perk, perkStyle, item: staticItem } = useBuildResolvers(runeTree, itemsMap)
</script>

<template>
  <main class="mx-auto max-w-6xl space-y-6 p-4 md:p-6">
    <PageHeader
      eyebrow="Builds & stats"
      title="Champions"
    >
      <!-- Position anchored left, champion search dead-center, rank + patch
           grouped right as compact secondary filters. The 1fr side columns
           keep the search centered regardless of how wide the flanks are;
           below md everything stacks. -->
      <div class="grid grid-cols-1 items-center gap-3 md:grid-cols-[1fr_auto_1fr]">
        <RolePicker
          class="justify-self-start"
          :position="selectedPosition"
          @update:position="selectPosition"
        />

        <ChampionPicker
          :champions="staticList ?? []"
          :champion-id="filterChampionId"
          placeholder="Search for a champion"
          trigger-class="w-56"
          @update:champion-id="selectChampion"
        />

        <div class="flex items-center gap-2 md:justify-self-end">
          <ChampionEloFilter
            size="sm"
            :model-value="selectedEloBracket"
            @update:model-value="onEloBracketChange"
          />

          <USelect
            :model-value="selectedPatch || undefined"
            :items="patchOptions"
            placeholder="Patch"
            size="sm"
            class="w-20"
            @update:model-value="onPatchChange"
          />
        </div>
      </div>
    </PageHeader>

    <!-- Wrap the data-dependent body in `<ClientOnly>` so the four lazy
         fetches (all `server: false`) never participate in the SSR render.
         Without this, race conditions between the `static-prefetch.client.ts`
         plugin priming the payload and the page's own `useLazyAsyncData`
         setup could leave the server rendering one tree (e.g. `<ul>`) while
         the client expected another (the skeleton), producing the
         hydration node mismatches reported in #149. The `<template #fallback>`
         renders the same skeleton list as the SSR shell so the user sees
         placeholder rows before the client takes over. -->
    <ClientOnly>
      <UAlert
        v-if="error"
        color="error"
        variant="soft"
        title="Failed to load champions"
        :description="error.message"
      />

      <!-- Cold load: placeholder rows in the real row layout so there's no
           blank area and no layout shift when the data resolves. Gated on all
           four sources (see `isPending`) so we never flash rows with fallback
           `Champion {id}` names or missing rune / item icons. -->
      <ul v-else-if="isPending" class="space-y-1" aria-hidden="true">
        <li v-for="i in PAGE_SIZE" :key="`skeleton-${i}`">
          <ChampionRowSkeleton />
        </li>
      </ul>

      <template v-else>
        <ul class="space-y-1">
          <li
            v-for="row in pagedRows"
            :key="`${row.championId}-${row.position}`"
          >
            <div
              role="button"
              tabindex="0"
              :aria-label="`View ${row.name} builds`"
              class="glass-hover flex cursor-pointer items-center gap-4 rounded-lg border border-default/60 bg-elevated/60 px-3 py-2.5 focus:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2 focus-visible:ring-offset-default"
              @click="onRowActivate(row)"
              @keydown.enter.prevent="onRowActivate(row)"
              @keydown.space.prevent="onRowActivate(row)"
            >
              <!-- Champion -->
              <div class="flex min-w-[10rem] items-center gap-2">
                <SkeletonImage
                  :src="row.iconUrl"
                  :alt="row.name"
                  width="36"
                  height="36"
                  class="size-9 rounded"
                />
                <span class="truncate font-medium">{{ row.name }}</span>
              </div>

              <!-- Position -->
              <SkeletonImage
                v-if="POSITION_BY_VALUE.get(row.position)?.iconUrl"
                :src="POSITION_BY_VALUE.get(row.position)!.iconUrl"
                :alt="row.position"
                :width="22"
                :height="22"
                class="size-[22px] shrink-0"
              />

              <!-- Runes: primary keystone with the secondary tree as a small
                   badge overlay — same presentation as the detail-page build
                   tabs (see ChampionBuildTabs leading slot). -->
              <div
                v-if="row.topBuild && perk(row.topBuild.primaryKeystoneId)"
                class="relative size-7 shrink-0"
              >
                <GameTooltipPerkIcon
                  :perk="perk(row.topBuild.primaryKeystoneId)"
                  :width="28"
                  :height="28"
                  class="size-7 rounded-full"
                />
                <GameTooltipPerkStyleIcon
                  v-if="perkStyle(row.topBuild.secondaryStyleId)"
                  :style="perkStyle(row.topBuild.secondaryStyleId)"
                  :width="16"
                  :height="16"
                  class="absolute -bottom-1 -right-2 size-4"
                />
              </div>

              <!-- Build path: reuse GameTooltipItemIcon so hover shows the
                   same item tooltip as the champion detail page. -->
              <div
                v-if="row.topBuild && row.topBuild.itemPath.length > 0"
                class="flex shrink-0 items-center gap-1"
              >
                <!-- Show the consensus path capped at 6 — the full ADC core
                     (Draven et al. reach 6) and the same worst case the detail
                     page's BuildPath lays out. ChampionBuildPathAnalyzer.WalkPath
                     can technically emit up to 7 (BuildItem0..6); the cap keeps
                     this shrink-0 row from widening on that rare case while
                     restoring the 6th item the old slice(0, 5) dropped. -->
                <template
                  v-for="(itemId, idx) in row.topBuild.itemPath.slice(0, 6)"
                  :key="`${row.championId}-${row.position}-bp-${idx}`"
                >
                  <GameTooltipItemIcon
                    :item="staticItem(itemId)"
                    :width="28"
                    :height="28"
                    class="size-7 rounded"
                  />
                  <UIcon
                    v-if="idx < Math.min(row.topBuild.itemPath.length, 6) - 1"
                    name="i-lucide-chevron-right"
                    class="size-3 text-dimmed"
                  />
                </template>
              </div>

              <!-- Tier: colour-coded S→D badge, sits in column order between the
                   lane and the win-rate. Computed server-side and bucketed by
                   patch-wide percentile (see ChampionTierCalculator). -->
              <div class="ml-auto flex min-w-[3rem] shrink-0 items-center justify-center">
                <TierBadge :tier="row.tier" />
              </div>

              <!-- Rates: bold whole-percent on top, small muted label below.
                   Numbers stay default-coloured — colour-coding tested too
                   noisy against the rest of the row. -->
              <div class="flex shrink-0 items-center gap-5 tabular-nums">
                <div class="flex min-w-[3rem] flex-col items-center">
                  <span class="text-lg font-bold leading-none">{{ formatPercentage(row.winRate, 0) }}</span>
                  <span class="mt-0.5 text-xs text-muted">WR</span>
                </div>
                <div class="flex min-w-[3rem] flex-col items-center">
                  <span class="text-lg font-bold leading-none">{{ formatPercentage(row.pickRate, 0) }}</span>
                  <span class="mt-0.5 text-xs text-muted">PR</span>
                </div>
              </div>
            </div>
          </li>
        </ul>

        <p
          v-if="filteredRows.length === 0"
          class="text-sm text-muted"
        >
          No champions match these filters.
        </p>

        <!-- Only show pagination when there's more than one page of results.
             This branch only renders once the data has resolved, so the count
             is never stale. -->
        <div
          v-if="totalCount > PAGE_SIZE"
          class="flex justify-center pt-2"
        >
          <UPagination
            :page="currentPage"
            :total="totalCount"
            :items-per-page="PAGE_SIZE"
            :sibling-count="1"
            color="neutral"
            variant="ghost"
            active-color="primary"
            active-variant="soft"
            @update:page="setPage"
          />
        </div>
      </template>

      <template #fallback>
        <ul class="space-y-1" aria-hidden="true">
          <li v-for="i in PAGE_SIZE" :key="`skeleton-${i}`">
            <ChampionRowSkeleton />
          </li>
        </ul>
      </template>
    </ClientOnly>
  </main>
</template>
