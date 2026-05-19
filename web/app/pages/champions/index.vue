<script setup lang="ts">
import type { ChampionSummaryResponse } from '~~/shared/types/champions'
import type { ChampionStaticListItem, RuneTreeResponse, StaticItemData } from '~~/shared/types/static-data'
import { formatPercentage } from '~~/shared/utils/ddragon'
import { POSITION_OPTIONS, isChampionPosition, type ChampionPosition } from '~/utils/positions'

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

function linkToFor(position: string) {
  return {
    path: `/champions/${position}`,
    query: {
      ...(selectedPatch.value ? { patch: selectedPatch.value } : {}),
      ...(position ? { position } : {}),
    },
  }
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
            icon="i-lucide-asterisk"
            square
            aria-label="All positions"
            @click="selectPosition(ALL_POSITIONS)"
          />
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

            <!-- Runes: primary keystone + secondary tree -->
            <div
              v-if="row.topBuild"
              class="flex shrink-0 items-center gap-1"
            >
              <NuxtImg
                v-if="perk(row.topBuild.primaryKeystoneId)?.iconUrl"
                :src="perk(row.topBuild.primaryKeystoneId)!.iconUrl"
                :alt="perk(row.topBuild.primaryKeystoneId)!.name"
                :width="28"
                :height="28"
                class="size-7 rounded-full"
              />
              <NuxtImg
                v-if="perkStyle(row.topBuild.secondaryStyleId)?.iconUrl"
                :src="perkStyle(row.topBuild.secondaryStyleId)!.iconUrl"
                :alt="perkStyle(row.topBuild.secondaryStyleId)!.name"
                :width="20"
                :height="20"
                class="size-5"
              />
            </div>

            <!-- Build path -->
            <div
              v-if="row.topBuild && row.topBuild.itemPath.length > 0"
              class="flex shrink-0 items-center gap-1"
            >
              <template
                v-for="(itemId, idx) in row.topBuild.itemPath.slice(0, 5)"
                :key="`${row.championId}-${row.position}-bp-${idx}`"
              >
                <NuxtImg
                  v-if="staticItem(itemId)?.iconUrl"
                  :src="staticItem(itemId)!.iconUrl"
                  :alt="staticItem(itemId)!.name"
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

            <!-- Rates -->
            <div class="ml-auto flex shrink-0 items-center gap-5 tabular-nums">
              <div class="text-right">
                <div class="text-xs text-muted">
                  Win rate
                </div>
                <div class="font-medium">
                  {{ formatPercentage(row.winRate) }}
                </div>
              </div>
              <div class="text-right">
                <div class="text-xs text-muted">
                  Pickrate
                </div>
                <div class="font-medium">
                  {{ formatPercentage(row.pickRate) }}
                </div>
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
