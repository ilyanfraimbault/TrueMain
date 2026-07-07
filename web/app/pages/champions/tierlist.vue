<script setup lang="ts">
import type { ChampionTierListResponse } from '~~/shared/types/champions'
import { POSITION_OPTIONS, isChampionPosition, type ChampionPosition } from '~/utils/positions'
import { ELO_BRACKET_ALL, normalizeEloBracket } from '~/utils/elo-brackets'

// Whole-percent format, same terse style as the /champions directory rows.
function pct(value: number): string {
  return `${Math.round(value * 100)}%`
}

useSeoMeta({
  title: 'Tier List · TrueMain',
  description: 'Champion meta tier list ranking champions by winrate and pickrate per role for the current patch.',
})

const { filters, setFilter } = useChampionFilters()

// null = "All positions" — same RolePicker contract as the /champions filter.
const selectedPosition = computed<ChampionPosition | null>(() => {
  const value = filters.value.position ?? ''
  return isChampionPosition(value) ? value : null
})

// ALL when the `?elo=` param is absent (the composable omits the default), so
// the picker always reflects a valid threshold.
const selectedEloBracket = computed<string>(() => normalizeEloBracket(filters.value.eloBracket))

// Tier list is computed server-side, so the fetch keys on patch + position +
// elo bracket and the backend does the per-role tiering. Client-only
// (`server: false`) to keep the SSR shell deterministic, mirroring the
// /champions directory page.
const {
  data: tierList,
  error: tierListError,
  status: tierListStatus,
} = useLazyAsyncData<ChampionTierListResponse>(
  () => `champion-tierlist-${filters.value.patch ?? 'latest'}-${selectedPosition.value ?? 'all'}-${filters.value.eloBracket ?? 'all'}`,
  () => {
    const query: Record<string, string> = {}
    if (filters.value.patch) query.patch = filters.value.patch
    if (selectedPosition.value) query.position = selectedPosition.value
    if (filters.value.eloBracket) query.eloBracket = filters.value.eloBracket
    return $fetch<ChampionTierListResponse>('/api/champions/tierlist', { query })
  },
  {
    watch: [() => filters.value.patch, selectedPosition, () => filters.value.eloBracket],
    server: false,
    default: () => ({ patchVersion: '', position: null, tiers: [] }),
  },
)

// Static champion list (names + icons) — shared composable so navigating between
// /champions and the tier list pays the fetch once (same key + options).
const {
  data: staticList,
  error: staticError,
  status: staticStatus,
} = useChampionStaticList()
const { data: versions } = useDDragonVersions()

const apiPatch = computed(() => tierList.value?.patchVersion ?? '')
const selectedPatch = computed(() => filters.value.patch || apiPatch.value || '')

const error = computed(() => tierListError.value ?? staticError.value)
const isLoadingStatus = (s: 'idle' | 'pending' | 'success' | 'error') => s === 'idle' || s === 'pending'
const isPending = computed(() =>
  isLoadingStatus(tierListStatus.value) || isLoadingStatus(staticStatus.value),
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

// Filter changes go through the shared composable so this page handles patch /
// position clearing exactly like the /champions directory and the champion
// detail pages (no pagination here, so setFilter is a drop-in — see #527).
function onPatchChange(value: unknown) {
  if (typeof value !== 'string' || !value) return
  void setFilter({ patch: value })
}

async function selectPosition(value: ChampionPosition | null) {
  await setFilter({ position: value })
}

function onEloBracketChange(value: string) {
  void setFilter({ eloBracket: value === ELO_BRACKET_ALL ? null : value })
}

const nameById = computed(() => new Map((staticList.value ?? []).map(item => [item.championId, item])))

// Flatten the tier groups into rows decorated with name + icon, carrying the
// tier letter so the template can render one badge per group and the row data.
const tierGroups = computed(() =>
  (tierList.value?.tiers ?? []).map(group => ({
    tier: group.tier,
    entries: group.entries.map((entry) => {
      const meta = nameById.value.get(entry.championId)
      return {
        ...entry,
        name: meta?.name ?? `Champion ${entry.championId}`,
        iconUrl: meta?.iconUrl ?? '',
      }
    }),
  })),
)

const hasRows = computed(() => tierGroups.value.some(group => group.entries.length > 0))

const positionByValue = new Map(POSITION_OPTIONS.map(option => [option.value as string, option]))

// Each row links to the champion page, pinned to the current patch + the row's
// own position — same destination shape as the /champions directory rows.
function championDestination(entry: { championId: number, position: string }) {
  return {
    path: `/champions/${entry.championId}`,
    query: {
      ...(selectedPatch.value ? { patch: selectedPatch.value } : {}),
      ...(entry.position ? { position: entry.position } : {}),
    },
  }
}
</script>

<template>
  <main class="mx-auto max-w-6xl space-y-6 p-4 md:p-6">
    <header class="space-y-3">
      <h1 class="text-2xl font-semibold">
        Tier List
      </h1>
      <p class="text-sm text-muted">
        Champions ranked into S–D tiers by winrate and pickrate for the current patch, per role.
      </p>

      <div class="flex flex-wrap items-center justify-between gap-3">
        <RolePicker
          :position="selectedPosition"
          @update:position="selectPosition"
        />

        <ChampionEloFilter
          :model-value="selectedEloBracket"
          @update:model-value="onEloBracketChange"
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

    <ClientOnly>
      <div class="h-0.5">
        <UProgress
          v-if="isPending"
          size="xs"
          color="primary"
          aria-label="Loading tier list"
        />
      </div>

      <UAlert
        v-if="error"
        color="error"
        variant="soft"
        title="Failed to load tier list"
        :description="error.message"
      />

      <template v-else>
        <div class="space-y-3">
          <SectionCard
            v-for="group in tierGroups"
            :key="group.tier"
          >
            <template #title>
              <div class="flex items-center gap-2">
                <TierBadge :tier="group.tier" />
                <span class="text-xs text-muted">{{ group.entries.length }} champions</span>
              </div>
            </template>

            <ul class="flex flex-wrap gap-2">
              <li
                v-for="entry in group.entries"
                :key="`${entry.championId}-${entry.position}`"
              >
                <NuxtLink
                  :to="championDestination(entry)"
                  :aria-label="`View ${entry.name} (${pct(entry.winRate)} WR, ${pct(entry.pickRate)} PR)`"
                  class="glass-hover flex items-center gap-2 rounded-md border border-default/60 bg-elevated/40 px-2 py-1.5"
                >
                  <SkeletonImage
                    :src="entry.iconUrl"
                    :alt="entry.name"
                    width="32"
                    height="32"
                    class="size-8 rounded"
                  />
                  <div class="flex flex-col">
                    <div class="flex items-center gap-1">
                      <span class="truncate text-sm font-medium">{{ entry.name }}</span>
                      <SkeletonImage
                        v-if="positionByValue.get(entry.position)?.iconUrl"
                        :src="positionByValue.get(entry.position)!.iconUrl"
                        :alt="entry.position"
                        :width="14"
                        :height="14"
                        class="size-[14px] shrink-0"
                      />
                    </div>
                    <span class="text-xs text-muted tabular-nums">
                      {{ pct(entry.winRate) }} WR · {{ pct(entry.pickRate) }} PR
                    </span>
                  </div>
                </NuxtLink>
              </li>
            </ul>
          </SectionCard>
        </div>

        <p
          v-if="!isPending && !hasRows"
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
            aria-label="Loading tier list"
          />
        </div>
      </template>
    </ClientOnly>
  </main>
</template>
