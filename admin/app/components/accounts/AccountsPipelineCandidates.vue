<script setup lang="ts">
// Candidates card of the Accounts hub's Pipeline tab: the `main_candidates` rows,
// searchable by Riot ID / PUUID / champion id, filterable by status + region,
// server-paged. A row click asks the tab to open the candidate slide-over. Split
// out of `AccountsPipeline.vue` (#1436); exposes `refresh`/`pending` for the
// hub's navbar button.
import type { TableColumn } from '@nuxt/ui'
import type { CandidateRow, MainCandidateStatus } from '~~/shared/types/ops'
import { formatDateTime, formatNumber } from '~~/shared/utils/format'

defineEmits<{
  select: [id: string]
}>()

const { nameFor, iconFor } = useChampionStatic()

// Status badge colors/icons live in `utils/candidate-status.ts` (auto-imported)
// so this page and the account explorer badge a status identically.
const candidateStatusItems = [
  { label: 'All statuses', value: ALL },
  ...CANDIDATE_STATUSES.map(status => ({ label: status, value: status })),
]

const candidateStatus = ref<'all' | MainCandidateStatus>(ALL)
const candidateRegion = ref<string>(ALL)
const candidateSearch = ref('')
// Debounce the search so we don't fire a request per keystroke.
const candidateSearchDebounced = refDebounced(candidateSearch, 300)
const candidatePage = ref(1)
const candidatePageSize = 25

// Reset to page 1 whenever a filter narrows/widens the result set.
watch([candidateStatus, candidateRegion, candidateSearchDebounced], () => {
  candidatePage.value = 1
})

const candidateFilters = computed(() => ({
  status: candidateStatus.value === ALL ? undefined : candidateStatus.value,
  region: candidateRegion.value === ALL ? undefined : candidateRegion.value,
  search: candidateSearchDebounced.value.trim() || undefined,
  page: candidatePage.value,
  pageSize: candidatePageSize,
}))

const hasCandidateFilters = computed(() =>
  candidateStatus.value !== ALL
  || candidateRegion.value !== ALL
  || Boolean(candidateSearch.value.trim()),
)
function resetCandidateFilters() {
  candidateStatus.value = ALL
  candidateRegion.value = ALL
  candidateSearch.value = ''
}

const {
  data: candidateData,
  pending: candidatePending,
  error: candidateError,
  refresh: refreshCandidates,
} = useCandidates(candidateFilters)

const candidateRows = computed<CandidateRow[]>(() => candidateData.value?.candidates ?? [])
const candidateTotal = computed(() => candidateData.value?.total ?? 0)

const candidateColumns: TableColumn<CandidateRow>[] = [
  { accessorKey: 'championId', header: 'Champion' },
  { accessorKey: 'riotId', header: 'Riot ID' },
  { accessorKey: 'platformId', header: 'Region' },
  { accessorKey: 'status', header: 'Status' },
  { accessorKey: 'score', header: 'Score' },
  { accessorKey: 'discoveredAtUtc', header: 'Discovered' },
  { accessorKey: 'validatedAtUtc', header: 'Validated' },
]

defineExpose({ refresh: refreshCandidates, pending: candidatePending })
</script>

<template>
  <UCard :ui="{ body: 'p-0 sm:p-0' }" class="mb-8">
    <template #header>
      <div class="flex flex-col gap-3">
        <div class="flex items-center justify-between gap-2">
          <PanelTitle
            title="Candidates"
            subtitle="Main-candidate ingestion pipeline · New → Scored → Queued → Processing → Validated."
          />
          <UBadge
            v-if="!candidatePending"
            color="neutral"
            variant="subtle"
            :label="`${formatNumber(candidateTotal)} total`"
          />
        </div>

        <!-- Candidate filters -->
        <div class="flex flex-wrap items-center gap-2">
          <UInput
            v-model="candidateSearch"
            icon="i-lucide-search"
            placeholder="Riot ID, PUUID or champion id"
            class="w-full sm:w-80"
            :loading="candidatePending"
          />
          <USelect
            v-model="candidateStatus"
            :items="candidateStatusItems"
            icon="i-lucide-filter"
            placeholder="Status"
            class="w-44"
          />
          <USelect
            v-model="candidateRegion"
            :items="REGION_ITEMS"
            icon="i-lucide-globe"
            placeholder="Region"
            class="w-40"
          />
          <UButton
            v-if="hasCandidateFilters"
            icon="i-lucide-x"
            color="neutral"
            variant="ghost"
            label="Clear"
            @click="resetCandidateFilters"
          />
        </div>
      </div>
    </template>

    <FetchErrorAlert
      v-if="candidateError"
      :error="candidateError"
      title="Failed to load candidates"
      class="m-4"
    />

    <UTable
      :data="candidateRows"
      :columns="candidateColumns"
      :loading="candidatePending"
      loading-color="primary"
      :ui="{ tr: 'cursor-pointer hover:bg-elevated/40', td: 'py-2' }"
      @select="(_event, row) => $emit('select', row.original.id)"
    >
      <template #championId-cell="{ row }">
        <div class="flex items-center gap-2.5">
          <NuxtImg
            v-if="iconFor(row.original.championId)"
            :src="iconFor(row.original.championId)!"
            :alt="nameFor(row.original.championId)"
            width="28"
            height="28"
            loading="lazy"
            class="size-7 rounded-md ring-1 ring-default"
          />
          <div
            v-else
            class="size-7 rounded-md bg-elevated ring-1 ring-default"
          />
          <span class="font-medium text-highlighted">
            {{ nameFor(row.original.championId) }}
          </span>
        </div>
      </template>
      <template #riotId-cell="{ row }">
        <span
          class="text-sm"
          :class="row.original.gameName ? 'text-default' : 'text-dimmed italic'"
        >
          {{ candidateRiotIdLabel(row.original.gameName, row.original.tagLine) }}
        </span>
      </template>
      <template #platformId-cell="{ row }">
        <span class="font-mono text-xs text-muted">{{ row.original.platformId }}</span>
      </template>
      <template #status-cell="{ row }">
        <UBadge
          :color="candidateStatusColor(row.original.status)"
          variant="subtle"
          size="sm"
          :icon="candidateStatusIcon(row.original.status)"
          :label="row.original.status"
        />
      </template>
      <template #score-cell="{ row }">
        <span class="tabular-nums text-sm">{{ row.original.score.toFixed(2) }}</span>
      </template>
      <template #discoveredAtUtc-cell="{ row }">
        <span class="tabular-nums text-xs text-muted">
          {{ formatDateTime(row.original.discoveredAtUtc) }}
        </span>
      </template>
      <template #validatedAtUtc-cell="{ row }">
        <span class="tabular-nums text-xs text-muted">
          {{ formatDateTime(row.original.validatedAtUtc) }}
        </span>
      </template>

      <template #empty>
        <div class="py-10 text-center text-sm text-muted">
          No candidates match these filters.
        </div>
      </template>
    </UTable>

    <!-- Pager -->
    <div
      v-if="candidateTotal > candidatePageSize"
      class="flex items-center justify-between gap-2 border-t border-default px-4 py-3"
    >
      <p class="text-xs text-muted tabular-nums">
        Page {{ candidatePage.toLocaleString('en-US') }} of
        {{ Math.max(1, Math.ceil(candidateTotal / candidatePageSize)).toLocaleString('en-US') }}
      </p>
      <UPagination
        v-model:page="candidatePage"
        :total="candidateTotal"
        :items-per-page="candidatePageSize"
        :sibling-count="1"
        active-color="primary"
        variant="subtle"
        :disabled="candidatePending"
        show-edges
      />
    </div>
  </UCard>
</template>
