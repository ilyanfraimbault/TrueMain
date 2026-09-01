<script setup lang="ts">
import type { ChampionTierListResponse } from '~~/shared/types/champions'
import { isChampionPosition, type ChampionPosition } from '~/utils/positions'
import { normalizeEloBracket } from '~/utils/elo-brackets'
import { isLoadingStatus } from '~/utils/async-data'
import { describeFetchError } from '~/utils/errors'

useSeoMeta({
  title: 'Champion Tier List',
  description: 'Champion meta tier list ranking champions by winrate and pickrate per role for the current patch.',
})

useSchemaOrg([
  defineWebPage({ name: 'Champion Tier List' }),
])

const { pathFor } = useChampionSlugs()

const { filters, setFilter } = useChampionFilters()

// null = "All positions" — same RolePicker contract as the /champions filter.
const selectedPosition = computed<ChampionPosition | null>(() => {
  const value = filters.value.position ?? ''
  return isChampionPosition(value) ? value : null
})

// The composable always resolves a concrete bracket (Master+ by default), so
// the picker always reflects the threshold actually being fetched.
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
  () => `champion-tierlist-${filters.value.patch ?? 'latest'}-${selectedPosition.value ?? 'all'}`
    + `-${filters.value.eloBracket ?? 'all'}-${filters.value.truemainsOnly ? 'truemains' : 'everyone'}`,
  () => {
    const query: Record<string, string> = {}
    if (filters.value.patch) query.patch = filters.value.patch
    if (selectedPosition.value) query.position = selectedPosition.value
    if (filters.value.eloBracket) query.eloBracket = filters.value.eloBracket
    // Sent only when off: true is the API default, so pinning it would just make
    // every resting request carry a redundant param.
    if (!filters.value.truemainsOnly) query.truemainsOnly = 'false'
    return $fetch<ChampionTierListResponse>('/api/champions/tierlist', { query })
  },
  {
    watch: [
      () => filters.value.patch,
      selectedPosition,
      () => filters.value.eloBracket,
      () => filters.value.truemainsOnly,
    ],
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
const isPending = computed(() =>
  isLoadingStatus(tierListStatus.value) || isLoadingStatus(staticStatus.value),
)

useErrorToast(error, { title: 'Failed to load tier list' })

const patchOptions = usePatchOptions(versions, apiPatch, () => filters.value.patch)

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
  // Pass the bracket through untouched — `null` would clear the param and land
  // back on the Master+ page default, making "All ranks" unselectable.
  void setFilter({ eloBracket: value })
}

const nameById = useChampionsById(staticList)

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

// Each chip links to the champion page, pinned to the current patch + the row's
// own position — same destination shape as the /champions directory rows.
function championDestination(entry: { championId: number, position: string }) {
  return {
    path: pathFor(entry.championId),
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
        Hover a champion for its win, pick and ban rate.
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

        <ChampionTruemainToggle
          :model-value="filters.truemainsOnly"
          @update:model-value="value => setFilter({ truemainsOnly: value })"
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
      <UAlert
        v-if="error"
        color="error"
        variant="soft"
        title="Failed to load tier list"
        :description="describeFetchError(error)"
      />

      <TierlistSkeleton v-else-if="isPending" />

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

            <!-- `gap-3` rather than the portrait's own spacing: the lane badge
                 overhangs the bottom-right corner, so neighbouring chips need
                 room for it not to collide with the next portrait. -->
            <ul class="flex flex-wrap gap-3">
              <li
                v-for="entry in group.entries"
                :key="`${entry.championId}-${entry.position}`"
              >
                <ChampionTierChip
                  :to="championDestination(entry)"
                  :name="entry.name"
                  :icon-url="entry.iconUrl"
                  :position="entry.position"
                  :win-rate="entry.winRate"
                  :pick-rate="entry.pickRate"
                  :ban-rate="entry.banRate"
                />
              </li>
            </ul>
          </SectionCard>
        </div>

        <p
          v-if="!hasRows"
          class="text-sm text-muted"
        >
          No champions match these filters.
        </p>
      </template>

      <template #fallback>
        <TierlistSkeleton />
      </template>
    </ClientOnly>
  </main>
</template>
