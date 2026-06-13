<script setup lang="ts">
import type { ChampionSummaryResponse } from '~~/shared/types/champions'
import type { ChampionStaticListItem, RuneTreeResponse, StaticItemData } from '~~/shared/types/static-data'
import { POSITION_OPTIONS, isChampionPosition, type ChampionPosition } from '~/utils/positions'

// Whole-percent format used for both WR and PR in the list — matches the
// terse style used by the in-game stats and the detail-page build tabs.
function pct(value: number): string {
  return `${Math.round(value * 100)}%`
}

// Mirrors the backend default; the page size is fixed in the UI (no
// per-page selector) so the only stateful pagination value carried in the
// URL is the page number.
const PAGE_SIZE = 50

useSeoMeta({
  title: 'Champions · TrueMain',
  description: 'Browse champions by lane with the most-played build, winrate and pickrate.',
})

const route = useRoute()
const router = useRouter()

const { filters } = useChampionFilters()

const nuxtApp = useNuxtApp()

// Current 1-indexed page, sourced from `?page=` so back/forward + direct
// links stay in sync with the list state. Coerce non-numeric or <1 values
// to 1 — that's the same clamping the backend does for safety, but doing
// it here keeps the URL stable while the page mounts.
const currentPage = computed<number>(() => {
  const raw = Array.isArray(route.query.page) ? route.query.page[0] : route.query.page
  const parsed = Number.parseInt(typeof raw === 'string' ? raw : '', 10)
  return Number.isFinite(parsed) && parsed >= 1 ? parsed : 1
})

async function setPage(next: number) {
  const clamped = Math.max(1, Math.floor(next))
  if (clamped === currentPage.value) return
  // Strip `?page=` when the user lands back on page 1 — keeps the URL
  // identical to the natural landing state instead of carrying a
  // redundant `?page=1`.
  const nextQuery = { ...route.query }
  if (clamped === 1) delete nextQuery.page
  else nextQuery.page = String(clamped)
  await router.replace({ query: nextQuery })
}

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
  () => `champions-list-${filters.value.patch ?? 'latest'}`,
  () => {
    const patch = filters.value.patch
    return $fetch<ChampionSummaryResponse[]>('/api/champions', {
      query: patch ? { patch } : {},
    })
  },
  { watch: [() => filters.value.patch], server: false, default: () => [] },
)
// Static fetches use `useLazyAsyncData` (not `useLazyFetch`) so the handler
// closure can call `markStaticFetched` after the network round trip — the
// `useFetch` wrapper hides that hook. `getCachedData` reuses entries across
// navigations within `STATIC_CACHE_TTL_MS` (see static-cache.ts).
const {
  data: staticList,
  error: staticError,
  status: staticStatus,
} = useLazyAsyncData<ChampionStaticListItem[]>(
  'champion-static-list',
  async () => {
    const data = await $fetch<ChampionStaticListItem[]>('/api/static/champions')
    markStaticFetched('champion-static-list', nuxtApp)
    return data
  },
  { getCachedData: key => getStaticCachedData(key, nuxtApp), server: false },
)
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
} = useLazyAsyncData<Record<number, StaticItemData>>(
  () => `static-items-${selectedPatch.value || 'pending'}`,
  async () => {
    const key = `static-items-${selectedPatch.value || 'pending'}`
    const data = await $fetch<Record<number, StaticItemData>>('/api/static/items', {
      query: { patch: selectedPatch.value },
    })
    markStaticFetched(key, nuxtApp)
    return data
  },
  {
    watch: [selectedPatch],
    immediate: false,
    default: () => ({}),
    getCachedData: key => getStaticCachedData(key, nuxtApp),
    server: false,
  },
)
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
} = useLazyAsyncData<RuneTreeResponse>(
  () => `rune-tree-${selectedPatch.value || 'latest'}`,
  async () => {
    const key = `rune-tree-${selectedPatch.value || 'latest'}`
    const data = await $fetch<RuneTreeResponse>('/api/static/rune-tree', {
      query: selectedPatch.value ? { patch: selectedPatch.value } : {},
    })
    markStaticFetched(key, nuxtApp)
    return data
  },
  {
    watch: [selectedPatch],
    getCachedData: key => getStaticCachedData(key, nuxtApp),
    server: false,
  },
)

const error = computed(() => summariesError.value ?? staticError.value ?? itemsError.value ?? runeTreeError.value)
// Treat the pre-fetch `'idle'` state from `useLazy*` the same as `'pending'`,
// otherwise the SSR shell briefly renders the empty `<ul>` (and the "No
// champions match…" copy below) before the client kicks off the first fetch.
// All four sources gate the skeleton so we never show rows with placeholder
// `Champion {id}` names or missing rune / item icons.
const isLoadingStatus = (s: 'idle' | 'pending' | 'success' | 'error') => s === 'idle' || s === 'pending'
const isPending = computed(() =>
  isLoadingStatus(summariesStatus.value)
  || isLoadingStatus(staticStatus.value)
  || isLoadingStatus(runeTreeStatus.value)
  || isLoadingStatus(itemsStatus.value),
)

const patchOptions = computed(() => {
  const seen = new Set<string>(
    (versions.value ?? [])
      .map(p => p.split('.').slice(0, 2).join('.'))
      .filter(Boolean)
      .slice(0, 12),
  )
  if (apiPatch.value) seen.add(apiPatch.value)
  if (filters.value.patch) seen.add(filters.value.patch)
  return [...seen]
    .map(p => ({ label: p, value: p }))
    .sort((a, b) => b.value.localeCompare(a.value, undefined, { numeric: true }))
})

// null = "All positions" — matches the RolePicker contract shared with
// the leaderboard filter strip.
const selectedPosition = computed<ChampionPosition | null>(() => {
  const value = filters.value.position ?? ''
  return isChampionPosition(value) ? value : null
})

// Champion filter sources from `?championId=` so deep links and back/forward
// keep the selection. Uses the same ChampionPicker as the truemain
// leaderboard so the UX matches across the two list pages.
const filterChampionId = computed<number | null>(() => {
  const raw = Array.isArray(route.query.championId) ? route.query.championId[0] : route.query.championId
  const parsed = Number.parseInt(typeof raw === 'string' ? raw : '', 10)
  return Number.isFinite(parsed) && parsed > 0 ? parsed : null
})

// Filter changes must reset to page 1 — otherwise switching from a
// 5-page result to a single-page one leaves `?page=4` in the URL and the
// list silently renders empty. Use a single router.replace so the patch /
// position / championId / page params transition atomically.
async function applyFilterReset(updates: {
  patch?: string | null
  position?: ChampionPosition | null
  championId?: number | null
}) {
  const nextQuery: Record<string, string> = {}
  for (const [key, value] of Object.entries(route.query)) {
    if (typeof value === 'string') nextQuery[key] = value
  }
  // Drop `?page=` on any filter change to anchor on page 1.
  delete nextQuery.page

  if (updates.patch !== undefined) {
    if (updates.patch) nextQuery.patch = updates.patch
    else delete nextQuery.patch
  }
  if (updates.position !== undefined) {
    if (!updates.position) delete nextQuery.position
    else nextQuery.position = updates.position
  }
  if (updates.championId !== undefined) {
    if (updates.championId) nextQuery.championId = String(updates.championId)
    else delete nextQuery.championId
  }
  await router.replace({ query: nextQuery })
}

function onPatchChange(value: unknown) {
  if (typeof value !== 'string' || !value) return
  void applyFilterReset({ patch: value })
}

async function selectPosition(value: ChampionPosition | null) {
  await applyFilterReset({ position: value })
}

async function selectChampion(value: number | null) {
  await applyFilterReset({ championId: value })
}

const baseRows = computed(() => {
  const nameById = new Map((staticList.value ?? []).map(item => [item.championId, item]))
  return (summaries.value ?? []).map(summary => ({
    ...summary,
    name: nameById.get(summary.championId)?.name ?? `Champion ${summary.championId}`,
    iconUrl: nameById.get(summary.championId)?.iconUrl ?? '',
  }))
})

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

const positionByValue = new Map(POSITION_OPTIONS.map(option => [option.value as string, option]))

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

function perk(id: number | undefined) {
  if (!id) return null
  return runeTree.value?.perks?.[id] ?? null
}
function perkStyle(id: number | undefined) {
  if (!id) return null
  return runeTree.value?.perkStyles?.[id] ?? null
}
function staticItem(id: number | undefined) {
  if (!id) return null
  return itemsMap.value?.[id] ?? null
}
</script>

<template>
  <main class="mx-auto max-w-6xl space-y-6 p-4 md:p-6">
    <header class="space-y-3">
      <h1 class="text-2xl font-semibold">
        Champions
      </h1>

      <div class="flex flex-wrap items-center justify-between gap-3">
        <RolePicker
          :position="selectedPosition"
          @update:position="selectPosition"
        />

        <ChampionPicker
          :champions="staticList ?? []"
          :champion-id="filterChampionId"
          placeholder="Search for a champion"
          trigger-class="w-64"
          @update:champion-id="selectChampion"
        />

        <USelect
          :model-value="selectedPatch || undefined"
          :items="patchOptions"
          placeholder="Patch"
          class="w-28"
          @update:model-value="onPatchChange"
        />
      </div>
    </header>

    <!-- Wrap the data-dependent body in `<ClientOnly>` so the four lazy
         fetches (all `server: false`) never participate in the SSR render.
         Without this, race conditions between the `static-prefetch.client.ts`
         plugin priming the payload and the page's own `useLazyAsyncData`
         setup could leave the server rendering one tree (e.g. `<ul>`) while
         the client expected another (the skeleton), producing the
         `<UProgress>` / `<ul>` hydration node mismatches reported in #149.
         The `<template #fallback>` matches the SSR shell so the user sees
         the same progress bar before the client takes over. -->
    <ClientOnly>
      <div class="h-0.5">
        <UProgress
          v-if="isPending"
          size="xs"
          color="primary"
          aria-label="Loading champions"
        />
      </div>

      <UAlert
        v-if="error"
        color="error"
        variant="soft"
        title="Failed to load champions"
        :description="error.message"
      />

      <template v-else>
        <!-- During fetch the UProgress bar above is the only loading signal;
             empty rectangles below it carried no information and looked worse
             than the implicit empty state. Rows mount as soon as summaries
             resolve, with per-icon SkeletonImage placeholders covering the
             remaining image loads. -->
        <ul class="space-y-1">
          <li
            v-for="row in pagedRows"
            :key="`${row.championId}-${row.position}`"
          >
            <div
              role="button"
              tabindex="0"
              :aria-label="`View ${row.name} builds`"
              class="glass-hover flex cursor-pointer items-center gap-4 rounded-md border border-default/60 bg-elevated/40 px-3 py-2 focus:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2 focus-visible:ring-offset-default"
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
                v-if="positionByValue.get(row.position)?.iconUrl"
                :src="positionByValue.get(row.position)!.iconUrl"
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
                  <span class="text-lg font-bold leading-none">{{ pct(row.winRate) }}</span>
                  <span class="mt-0.5 text-xs text-muted">WR</span>
                </div>
                <div class="flex min-w-[3rem] flex-col items-center">
                  <span class="text-lg font-bold leading-none">{{ pct(row.pickRate) }}</span>
                  <span class="mt-0.5 text-xs text-muted">PR</span>
                </div>
              </div>
            </div>
          </li>
        </ul>

        <p
          v-if="!isPending && filteredRows.length === 0"
          class="text-sm text-muted"
        >
          No champions match these filters.
        </p>

        <!-- Only show pagination when there's more than one page of results
             on the backend. The component is hidden during the pending state
             to avoid flashing stale page counts between fetches. -->
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
        <div class="h-0.5">
          <UProgress
            size="xs"
            color="primary"
            aria-label="Loading champions"
          />
        </div>
      </template>
    </ClientOnly>
  </main>
</template>
