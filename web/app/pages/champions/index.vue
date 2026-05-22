<script setup lang="ts">
import type { ChampionSummaryResponse } from '~~/shared/types/champions'
import type { ChampionStaticListItem, RuneTreeResponse, StaticItemData } from '~~/shared/types/static-data'
import { getPositionIconUrl } from '~~/shared/utils/ddragon'
import { POSITION_OPTIONS, isChampionPosition, type ChampionPosition } from '~/utils/positions'

// Whole-percent format used for both WR and PR in the list — matches the
// terse style used by the in-game stats and the detail-page build tabs.
function pct(value: number): string {
  return `${Math.round(value * 100)}%`
}

const FILL_POSITION_ICON_URL = getPositionIconUrl('fill')

useSeoMeta({
  title: 'Champions · TrueMain',
  description: 'Browse champions by lane with the most-played build, winrate and pickrate.',
})

const route = useRoute()
const router = useRouter()

const { filters, setFilter } = useChampionFilters()

const nuxtApp = useNuxtApp()

// All four fetches are client-only (`server: false`) so SSR ships a
// deterministic empty shell under the skeleton/progress bar instead of
// racing the data into the rendered HTML — without it, fast local API
// responses resolved before the SSR render completed, baking `isPending=false`
// into the server output while the client hydrated with `isPending=true`,
// producing `<!-- -->` vs `<div>` and `<ul>` vs `<div>` hydration mismatches.
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
  { watch: [() => filters.value.patch], server: false },
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

const ALL_POSITIONS = 'all' as const
const selectedPosition = computed<ChampionPosition | typeof ALL_POSITIONS>(() => {
  const value = filters.value.position ?? ''
  return isChampionPosition(value) ? value : ALL_POSITIONS
})

function onPatchChange(value: unknown) {
  if (typeof value !== 'string' || !value) return
  setFilter(value, null)
}

async function selectPosition(value: ChampionPosition | typeof ALL_POSITIONS) {
  if (value === ALL_POSITIONS) {
    // useChampionFilters.setFilter() can only add, not clear, so strip the
    // position param via router directly.
    const next = { ...route.query }
    delete next.position
    await router.replace({ query: next })
    return
  }
  await setFilter(null, value)
}

const searchQuery = ref('')

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
  if (pos !== ALL_POSITIONS) rows = rows.filter(row => row.position === pos)
  const q = searchQuery.value.trim().toLowerCase()
  if (q) rows = rows.filter(row => row.name.toLowerCase().includes(q))
  return rows
})

const positionByValue = new Map(POSITION_OPTIONS.map(option => [option.value as string, option]))

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
        <UFieldGroup size="md">
          <UButton
            :variant="selectedPosition === ALL_POSITIONS ? 'soft' : 'ghost'"
            color="neutral"
            square
            aria-label="All positions"
            @click="selectPosition(ALL_POSITIONS)"
          >
            <SkeletonImage
              :src="FILL_POSITION_ICON_URL"
              alt="All positions"
              :width="18"
              :height="18"
              class="size-[18px]"
            />
          </UButton>
          <UButton
            v-for="option in POSITION_OPTIONS"
            :key="option.value"
            :variant="selectedPosition === option.value ? 'soft' : 'ghost'"
            color="neutral"
            square
            :aria-label="option.label"
            @click="selectPosition(option.value)"
          >
            <SkeletonImage
              :src="option.iconUrl"
              :alt="option.label"
              :width="18"
              :height="18"
              class="size-[18px]"
            />
          </UButton>
        </UFieldGroup>

        <UInput
          v-model="searchQuery"
          icon="i-lucide-search"
          placeholder="Search champion…"
          class="min-w-[16rem] max-w-md flex-1"
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
         the same skeleton + progress bar before the client takes over. -->
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
        <!-- Skeleton stays up while the list refetches (initial load OR patch
             swap) instead of leaving stale rows on screen with no signal —
             matches the "fluid loading state" the user expects. -->
        <div
          v-if="isPending"
          class="space-y-2"
        >
          <USkeleton
            v-for="i in 6"
            :key="i"
            class="h-14 w-full rounded"
          />
        </div>

        <ul
          v-else
          class="space-y-1"
        >
          <li
            v-for="row in filteredRows"
            :key="`${row.championId}-${row.position}`"
          >
            <NuxtLink
              :to="{ path: `/champions/${row.championId}`, query: { ...(selectedPatch ? { patch: selectedPatch } : {}), ...(row.position ? { position: row.position } : {}) } }"
              class="flex items-center gap-4 rounded-md border border-default/60 bg-elevated/40 px-3 py-2 transition-colors hover:bg-elevated/80"
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
                <template
                  v-for="(itemId, idx) in row.topBuild.itemPath.slice(0, 5)"
                  :key="`${row.championId}-${row.position}-bp-${idx}`"
                >
                  <GameTooltipItemIcon
                    :item="staticItem(itemId)"
                    :width="28"
                    :height="28"
                    class="size-7 rounded"
                  />
                  <UIcon
                    v-if="idx < Math.min(row.topBuild.itemPath.length, 5) - 1"
                    name="i-lucide-chevron-right"
                    class="size-3 text-dimmed"
                  />
                </template>
              </div>

              <!-- Rates: bold whole-percent on top, small muted label below.
                   Numbers stay default-coloured — colour-coding tested too
                   noisy against the rest of the row. -->
              <div class="ml-auto flex shrink-0 items-center gap-5 tabular-nums">
                <div class="flex min-w-[3rem] flex-col items-center">
                  <span class="text-lg font-bold leading-none">{{ pct(row.winRate) }}</span>
                  <span class="mt-0.5 text-xs text-muted">WR</span>
                </div>
                <div class="flex min-w-[3rem] flex-col items-center">
                  <span class="text-lg font-bold leading-none">{{ pct(row.pickRate) }}</span>
                  <span class="mt-0.5 text-xs text-muted">PR</span>
                </div>
              </div>
            </NuxtLink>
          </li>
        </ul>

        <p
          v-if="!isPending && filteredRows.length === 0"
          class="text-sm text-muted"
        >
          No champions match these filters.
        </p>
      </template>

      <template #fallback>
        <div class="h-0.5">
          <UProgress
            size="xs"
            color="primary"
            aria-label="Loading champions"
          />
        </div>
        <div class="space-y-2">
          <USkeleton
            v-for="i in 6"
            :key="i"
            class="h-14 w-full rounded"
          />
        </div>
      </template>
    </ClientOnly>
  </main>
</template>
