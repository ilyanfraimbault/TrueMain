<script setup lang="ts">
import { championDisplayName } from '#shared/utils/champion-name'
// The tier list, as the site and the desktop app both render it (#1732): each
// app's route file wraps this with its own head tags and nothing else.
import type { ChampionTierListResponse } from '#shared/types/champions'
import { isChampionPosition, type ChampionPosition } from '#common/utils/positions'
import { isLoadingStatus } from '#common/utils/async-data'
import { describeStaleFetchError } from '#common/utils/errors'

const { pathFor } = useChampionSlugs()

const { filters } = useChampionFilters()

// null = "All positions" — same RolePicker contract as the /champions filter.
const selectedPosition = computed<ChampionPosition | null>(() => {
  const value = filters.value.position ?? ''
  return isChampionPosition(value) ? value : null
})

// Tier list is computed server-side, so the fetch keys on patch + position +
// elo bracket and the backend does the per-role tiering. Client-only
// (`server: false`) to keep the SSR shell deterministic, mirroring the
// /champions directory page.
const apiFetch = useApiFetch()
const tierListFetch = useAsyncData<ChampionTierListResponse>(
  () => `champion-tierlist-${filters.value.patch ?? 'latest'}-${selectedPosition.value ?? 'all'}`
    + `-${filters.value.eloBracket ?? 'all'}-${filters.value.truemainsOnly ? 'truemains' : 'everyone'}`,
  (_nuxtApp, { signal }) => {
    const query: Record<string, string> = {}
    if (filters.value.patch) query.patch = filters.value.patch
    if (selectedPosition.value) query.position = selectedPosition.value
    if (filters.value.eloBracket) query.eloBracket = filters.value.eloBracket
    // Sent only when off: true is the API default, so pinning it would just make
    // every resting request carry a redundant param.
    if (!filters.value.truemainsOnly) query.truemainsOnly = 'false'
    return apiFetch<ChampionTierListResponse>('/champions/tierlist', { query, signal })
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
const { status: tierListStatus, refresh: refreshTierList } = tierListFetch
// A failed filter click keeps the previous tiers on screen and answers with a
// toast (#1668); only a failed first load is the inline alert.
const actionToast = useActionToast()
const { data: tierList, error: tierListError, staleError: tierListStaleError } = useRefetchFallback(tierListFetch, {
  onStaleFailure: failure => actionToast.failure('Could not update the tier list', describeStaleFetchError(failure)),
})

// Static champion list (names + icons) — shared composable so navigating between
// /champions and the tier list pays the fetch once (same key + options).
const {
  data: staticList,
  error: staticError,
  status: staticStatus,
} = useChampionStaticList()

const apiPatch = computed(() => tierList.value?.patchVersion ?? '')
const selectedPatch = computed(() => filters.value.patch || apiPatch.value || '')

const error = computed(() => tierListError.value ?? staticError.value)
const isPending = computed(() =>
  isLoadingStatus(tierListStatus.value) || isLoadingStatus(staticStatus.value),
)

// Same `?championId=` search as the /champions directory: it narrows the tiers
// to that champion's lines instead of refetching.
const filterChampionId = useRouteQueryChampionId()

const nameById = useChampionsById(staticList)

// Flatten the tier groups into rows decorated with name + icon, carrying the
// tier letter so the template can render one badge per group and the row data.
const tierGroups = computed(() =>
  (tierList.value?.tiers ?? [])
    .map(group => ({
      tier: group.tier,
      entries: group.entries
        .filter(entry => filterChampionId.value === null || entry.championId === filterChampionId.value)
        .map((entry) => {
          const meta = nameById.value.get(entry.championId)
          return {
            ...entry,
            name: championDisplayName(entry.championId, meta),
            iconUrl: meta?.iconUrl ?? '',
          }
        }),
    }))
    .filter(group => group.entries.length > 0),
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

// A client-side navigation keeps the outgoing page under the loading bar until
// the tier list is in (#1689); the static lookups keep their skeleton.
await tierListFetch
</script>

<template>
  <div class="mx-auto max-w-6xl space-y-6 p-4 md:p-6">
    <!-- Same header and filter row as the /champions directory, so moving
         between the two pages keeps every control in place. -->
    <PageHeader
      eyebrow="Meta"
      title="Tier List"
    >
      <ChampionListFilters
        :champions="staticList ?? []"
        :api-patch="apiPatch"
      />
    </PageHeader>

    <ClientOnly>
      <FetchErrorAlert
        v-if="error"
        :error="error"
        title="Failed to load the tier list"
      />

      <TierlistSkeleton v-else-if="isPending" />

      <template v-else>
        <StaleContentNotice
          :error="tierListStaleError"
          subject="the previous tiers"
          :on-retry="() => refreshTierList()"
        />

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
  </div>
</template>
