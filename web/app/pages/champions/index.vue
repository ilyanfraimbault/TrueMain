<script setup lang="ts">
import { isChampionPosition, type ChampionPosition } from '#common/utils/positions'
import { normalizeEloBracket } from '#common/utils/elo-brackets'
import { isLoadingStatus } from '#common/utils/async-data'
import { firstParamValue } from '#common/utils/route-params'
import { directoryOrderToQuery, parseDirectoryOrder, type DirectoryOrder } from '~/utils/table-sorting'

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

// Every fetch here is client-only (`server: false`) so SSR ships a
// deterministic shell under the skeleton instead of racing the data into the
// rendered HTML — without it, fast local API responses resolved before the SSR
// render completed and the server baked a different branch than the client
// hydrated, producing node mismatches (#149).
//
// The directory comes one page at a time (#1734): the API filters by lane and
// champion, orders by the table's sort and pages, so the page never downloads
// the whole directory.
const route = useRoute()
const filterChampionId = useRouteQueryChampionId()
const order = computed<DirectoryOrder>(() =>
  parseDirectoryOrder(firstParamValue(route.query.sort), firstParamValue(route.query.order)))

const selectedPosition = computed<ChampionPosition | null>(() => {
  const value = filters.value.position ?? ''
  return isChampionPosition(value) ? value : null
})

const directory = useChampionDirectory({
  patch: () => filters.value.patch,
  eloBracket: () => filters.value.eloBracket,
  truemainsOnly: () => filters.value.truemainsOnly,
  position: selectedPosition,
  championId: filterChampionId,
  order,
  page: currentPage,
  pageSize: PAGE_SIZE,
})
const { rows: summaries, total, patchVersion, error: directoryError } = directory

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

const apiPatch = computed(() => patchVersion.value)
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

const error = computed(() => directoryError.value ?? staticError.value ?? itemsError.value ?? runeTreeError.value)

// The skeleton covers the first load only — the first page of the directory
// and the lookups its rows draw from (names, rune and item icons), so no row
// ever flashes a fallback `Champion {id}` name or an empty icon. Later filter,
// sort or page changes keep the rows on screen under the table's loading bar.
// `idle` counts as loading (see isLoadingStatus): the client kicks the static
// fetches off after mount.
const lookupsLoading = computed(() =>
  isLoadingStatus(staticStatus.value)
  || isLoadingStatus(runeTreeStatus.value)
  || isLoadingStatus(itemsStatus.value))
const lookupsSettled = ref(false)
watch(lookupsLoading, (loading) => {
  if (!loading) lookupsSettled.value = true
}, { immediate: true })
const isColdLoading = computed(() => directory.isInitialLoading.value || !lookupsSettled.value)

const patchOptions = usePatchOptions(versions, apiPatch, () => filters.value.patch)


// The composable always resolves a concrete bracket (Master+ by default), so
// the picker always reflects the threshold actually being fetched.
const selectedEloBracket = computed<string>(() => normalizeEloBracket(filters.value.eloBracket))


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

const rows = computed(() =>
  summaries.value.map(summary => ({
    ...summary,
    name: championsById.value.get(summary.championId)?.name ?? `Champion ${summary.championId}`,
    iconUrl: championsById.value.get(summary.championId)?.iconUrl ?? '',
  })),
)

// The table's sortable headers: one replace for the column and the direction,
// back to page 1 (a new order reshuffles every page). The defaults leave the
// URL bare.
const setQueryFilters = useRouteFiltersSetter()
function setOrder(next: DirectoryOrder) {
  void setQueryFilters(directoryOrderToQuery(next))
}

// A page past the end — a hand-typed `?page=`, or a filter that shrank the
// list under it — comes back empty with the real total: step back to page 1.
watch([summaries, total], ([lines, count]) => {
  if (lines.length === 0 && count > 0 && currentPage.value > 1) void setPage(1)
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
await directory.ready
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
        <ChampionDirectoryTable
          v-if="isColdLoading || rows.length > 0"
          :rows="rows"
          :offset="(currentPage - 1) * PAGE_SIZE"
          :order="order"
          :loading="isColdLoading"
          :refreshing="directory.isLoading.value"
          :skeleton-rows="PAGE_SIZE"
          :destination="rowDestination"
          :rune-tree="runeTree"
          :items-map="itemsMap"
          @update:order="setOrder"
        />

        <p v-else class="text-sm text-muted">
          No champions match these filters.
        </p>

        <!-- Only when there is more than one page, and only once the data is in. -->
        <div
          v-if="!isColdLoading && total > PAGE_SIZE"
          class="flex justify-center pt-2"
        >
          <UPagination
            :page="currentPage"
            :total="total"
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
          :order="order"
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
