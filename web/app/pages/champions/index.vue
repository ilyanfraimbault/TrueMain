<script setup lang="ts">
import type { ChampionSummaryResponse } from '~~/shared/types/champions'
import type { ChampionStaticListItem, RuneTreeResponse, StaticItemData } from '~~/shared/types/static-data'
import { formatPercentage, getPositionIconUrl } from '~~/shared/utils/ddragon'
import { POSITION_OPTIONS, isChampionPosition, type ChampionPosition } from '~/utils/positions'

// Color thresholds for the inline win-rate / pick-rate badges. Tuned to feel
// like the in-client stats colors: clearly positive above the upper bound,
// neutral text in the middle band, distinctly negative below the lower bound.
function winRateColor(value: number): string {
  if (value >= 0.53) return 'text-emerald-400'
  if (value < 0.48) return 'text-rose-400'
  return 'text-default'
}
function pickRateColor(value: number): string {
  if (value >= 0.08) return 'text-emerald-400'
  if (value < 0.03) return 'text-rose-400'
  return 'text-default'
}

const FILL_POSITION_ICON_URL = getPositionIconUrl('fill')

useSeoMeta({
  title: 'Champions · TrueMain',
  description: 'Browse champions by lane with the most-played build, winrate and pickrate.',
})

const route = useRoute()
const router = useRouter()

const { filters, setFilter } = useChampionFilters()

const {
  data: summaries,
  error: summariesError,
  status: summariesStatus,
} = await useAsyncData<ChampionSummaryResponse[]>(
  () => `champions-list-${filters.value.patch ?? 'latest'}`,
  () => {
    const patch = filters.value.patch
    return $fetch<ChampionSummaryResponse[]>('/api/champions', {
      query: patch ? { patch } : {},
    })
  },
  { watch: [() => filters.value.patch] },
)
const { data: staticList, error: staticError } = await useFetch<ChampionStaticListItem[]>(
  '/api/static/champions',
  { key: 'champion-static-list' },
)
const { data: itemsMap, error: itemsError } = await useFetch<Record<number, StaticItemData>>(
  '/api/static/items',
  { key: 'static-items' },
)
const { data: runeTree, error: runeTreeError } = await useFetch<RuneTreeResponse>(
  '/api/static/rune-tree',
  { key: 'rune-tree' },
)
const { data: versions } = useDDragonVersions()

const error = computed(() => summariesError.value ?? staticError.value ?? itemsError.value ?? runeTreeError.value)
const isPending = computed(() => summariesStatus.value === 'pending')

const apiPatch = computed(() => summaries.value?.[0]?.patchVersion ?? '')

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

const selectedPatch = computed(() => filters.value.patch || apiPatch.value || '')

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
      <div class="space-y-1">
        <h1 class="text-2xl font-semibold">
          Champions
        </h1>
        <p class="text-sm text-muted">
          One row per champion and lane. The build shown is the most-played for the selected patch.
        </p>
      </div>

      <div class="flex flex-wrap items-center justify-between gap-3">
        <UFieldGroup size="md">
          <UButton
            :variant="selectedPosition === ALL_POSITIONS ? 'soft' : 'ghost'"
            :color="selectedPosition === ALL_POSITIONS ? 'primary' : 'neutral'"
            square
            aria-label="All positions"
            @click="selectPosition(ALL_POSITIONS)"
          >
            <NuxtImg
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
            :color="selectedPosition === option.value ? 'primary' : 'neutral'"
            square
            :aria-label="option.label"
            @click="selectPosition(option.value)"
          >
            <NuxtImg
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

    <UAlert
      v-if="error"
      color="error"
      variant="soft"
      title="Failed to load champions"
      :description="error.message"
    />

    <template v-else>
      <div
        v-if="isPending && filteredRows.length === 0"
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
            <NuxtImg
              v-if="positionByValue.get(row.position)?.iconUrl"
              :src="positionByValue.get(row.position)!.iconUrl"
              :alt="row.position"
              :width="22"
              :height="22"
              class="size-[22px] shrink-0"
            />

            <!-- Runes: primary keystone + secondary tree (same tooltip
                 components as the champion detail page so hover shows the
                 full perk / style description). -->
            <div
              v-if="row.topBuild"
              class="flex shrink-0 items-center gap-1"
            >
              <GameTooltipPerkIcon
                :perk="perk(row.topBuild.primaryKeystoneId)"
                :width="28"
                :height="28"
                class="size-7 rounded-full"
              />
              <GameTooltipPerkStyleIcon
                :style="perkStyle(row.topBuild.secondaryStyleId)"
                :width="20"
                :height="20"
                class="size-5"
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

            <!-- Rates: large coloured percentage on top, small muted label
                 below. Color thresholds chosen to feel like the in-client
                 stats colors. -->
            <div class="ml-auto flex shrink-0 items-center gap-5 tabular-nums">
              <div class="flex min-w-[3rem] flex-col items-center">
                <span
                  class="text-lg font-semibold leading-none"
                  :class="winRateColor(row.winRate)"
                >
                  {{ formatPercentage(row.winRate) }}
                </span>
                <span class="mt-0.5 text-xs text-muted">WR</span>
              </div>
              <div class="flex min-w-[3rem] flex-col items-center">
                <span
                  class="text-lg font-semibold leading-none"
                  :class="pickRateColor(row.pickRate)"
                >
                  {{ formatPercentage(row.pickRate) }}
                </span>
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
  </main>
</template>
