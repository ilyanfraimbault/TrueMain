<script setup lang="ts">
import type { ChampionSummaryResponse } from '~~/shared/types/champions'
import { isChampionPosition, type ChampionPosition } from '#common/utils/positions'
import { normalizeEloBracket } from '#common/utils/elo-brackets'
import { isLoadingStatus } from '#common/utils/async-data'

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

const { pathFor } = useChampionSlugs()

const { filters, setFilter } = useChampionFilters()

const { currentPage, setPage } = useRoutePage()

// All four fetches are client-only (`server: false`) so SSR ships a
// deterministic empty shell under the skeleton/progress bar instead of
// racing the data into the rendered HTML — without it, fast local API
// responses resolved before the SSR render completed, baking `isPending=false`
// into the server output while the client hydrated with `isPending=true`,
// producing `<!-- -->` vs `<div>` and `<ul>` vs `<div>` hydration mismatches.
//
// The endpoint returns the full directory (~500 rows on a populated patch). Pagination
// is applied client-side below so search + position filters can stay client-side too
// and the user can paginate filtered subsets without extra round-trips.
const apiFetch = useApiFetch()
const summariesFetch = useAsyncData<ChampionSummaryResponse[]>(
  () => `champions-list-${filters.value.patch ?? 'latest'}-${filters.value.eloBracket ?? 'ALL'}`
    + `-${filters.value.truemainsOnly ? 'truemains' : 'everyone'}`,
  (_nuxtApp, { signal }) => {
    const patch = filters.value.patch
    const elo = filters.value.eloBracket
    return apiFetch<ChampionSummaryResponse[]>('/champions', {
      query: {
        ...(patch ? { patch } : {}),
        // Cumulative "X+" threshold; the composable already omits the default
        // ALL, so a value here is always a real filter the backend expands.
        ...(elo ? { eloBracket: elo } : {}),
        // Sent only when off — true is the API default.
        ...(filters.value.truemainsOnly ? {} : { truemainsOnly: 'false' }),
      },
      signal,
    })
  },
  {
    watch: [
      () => filters.value.patch,
      () => filters.value.eloBracket,
      () => filters.value.truemainsOnly,
    ],
    server: false,
    default: () => [],
  },
)
const { data: summaries, error: summariesError, status: summariesStatus } = summariesFetch
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

// Item icons are patch-specific, so the fetch follows the patch the list shows.
// `immediate: false` + the watcher defers it until `selectedPatch` is known,
// rather than fetching `static-items-latest` and refetching under the patch.
const {
  data: itemsMap,
  error: itemsError,
  status: itemsStatus,
  execute: fetchItems,
} = useStaticItems(selectedPatch, { immediate: false, unresolvedKeySegment: 'pending' })
watch(selectedPatch, (patch) => {
  if (patch) void fetchItems()
}, { immediate: true })

// Rune tree pinned to the list's patch: IPX then hits CommunityDragon's
// per-patch (year-cacheable) tree instead of `latest`, a moving target.
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

// The composable always resolves a concrete bracket (Master+ by default), so
// the picker always reflects the threshold actually being fetched.
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

// The row's destination: the champion page on its lane, at the patch the list
// shows. The row itself navigates (a button-style target, per #147).
function rowDestination(row: { championId: number, position: string }) {
  return {
    path: pathFor(row.championId),
    query: {
      ...(selectedPatch.value ? { patch: selectedPatch.value } : {}),
      ...(row.position ? { position: row.position } : {}),
    },
  }
}

// A client-side navigation waits under the loading bar for the directory (#1689).
await summariesFetch
</script>

<template>
  <div class="mx-auto max-w-6xl space-y-6 p-4 md:p-6">
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

          <ChampionTruemainToggle
            :model-value="filters.truemainsOnly"
            @update:model-value="value => setFilter({ truemainsOnly: value }, { resetPage: true })"
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

    <!-- Wrap the data-dependent body in `<ClientOnly>` so the four
         fetches (all `server: false`) never participate in the SSR render —
         otherwise the server and the client could render different trees and
         produce the hydration node mismatches reported in #149. The fallback
         is the same skeleton table the client shows while it loads. -->
    <ClientOnly>
      <FetchErrorAlert
        v-if="error"
        :error="error"
        title="Failed to load the champion list"
      />

      <template v-else>
        <!-- Gated on all four sources (see `isPending`) so no row flashes a
             fallback `Champion {id}` name or a missing rune / item icon. -->
        <ChampionDirectoryTable
          v-if="isPending || pagedRows.length > 0"
          :rows="pagedRows"
          :offset="(currentPage - 1) * PAGE_SIZE"
          :loading="isPending"
          :skeleton-rows="PAGE_SIZE"
          :destination="rowDestination"
          :rune-tree="runeTree"
          :items-map="itemsMap"
        />

        <p v-else class="text-sm text-muted">
          No champions match these filters.
        </p>

        <!-- Only when there is more than one page, and only once the data is in. -->
        <div
          v-if="!isPending && totalCount > PAGE_SIZE"
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
        <ChampionDirectoryTable
          :rows="[]"
          :offset="0"
          loading
          :skeleton-rows="PAGE_SIZE"
          :destination="rowDestination"
          :rune-tree="null"
          :items-map="null"
        />
      </template>
    </ClientOnly>
  </div>
</template>
