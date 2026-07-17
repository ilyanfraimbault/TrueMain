<script setup lang="ts">
// Candidates panel — two read-only diagnostic views over the ingestion pipeline:
//   1. Candidates (`main_candidates`): New → Scored → Queued → Processing →
//      Validated (or Rejected). Searchable by Riot ID / PUUID / champion id,
//      filterable by status + region, server-paged. Row click opens a slide-over
//      with the exact pipeline stage, timestamps, ingested match count and the
//      linked manual seed request (if one brought the account in).
//   2. Demandes d'ajout (`seed_requests`): the manual "add a main" intake list,
//      Pending → Resolving → Ingested / Failed, with the Riot ID, region,
//      requested/processed times and any error. Filterable by status + search.
// Read-only — no write actions in v1 (use "Add mains" to queue a request).
import type { TableColumn } from '@nuxt/ui'
import type {
  BadgeColor,
  CandidateDetail,
  CandidateRow,
  MainCandidateStatus,
  SeedRequestReadModel,
  SeedRequestStatus,
} from '~~/shared/types/ops'
import { formatDateTime, formatNumber } from '~~/shared/utils/format'

const { nameFor, iconFor } = useChampionStatic()

// =============================================================================
// View 1 — Candidates
// =============================================================================
const candidateStatuses: MainCandidateStatus[] = [
  'New',
  'Scored',
  'Queued',
  'Processing',
  'Validated',
  'Rejected',
]

// Pipeline-stage badge colors trace New → Validated/Rejected (kept close to the
// Overview candidate-pipeline palette so the two panels read consistently).
const CANDIDATE_STATUS_COLOR: Record<MainCandidateStatus, BadgeColor> = {
  New: 'neutral',
  Scored: 'info',
  Queued: 'warning',
  Processing: 'warning',
  Validated: 'success',
  Rejected: 'error',
}
const CANDIDATE_STATUS_ICON: Record<MainCandidateStatus, string> = {
  New: 'i-lucide-sparkles',
  Scored: 'i-lucide-calculator',
  Queued: 'i-lucide-list-ordered',
  Processing: 'i-lucide-loader',
  Validated: 'i-lucide-circle-check',
  Rejected: 'i-lucide-circle-x',
}
function candidateStatusColor(status: MainCandidateStatus): BadgeColor {
  return CANDIDATE_STATUS_COLOR[status] ?? 'neutral'
}
function candidateStatusIcon(status: MainCandidateStatus): string {
  return CANDIDATE_STATUS_ICON[status] ?? 'i-lucide-circle'
}

const candidateStatusItems = [
  { label: 'All statuses', value: ALL },
  ...candidateStatuses.map(status => ({ label: status, value: status })),
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

// Riot ID display: "gameName#tagLine" when resolved, else an em dash.
function riotIdLabel(gameName: string | null, tagLine: string | null): string {
  if (!gameName) {
    return '—'
  }
  return tagLine ? `${gameName}#${tagLine}` : gameName
}

const candidateColumns: TableColumn<CandidateRow>[] = [
  { accessorKey: 'championId', header: 'Champion' },
  { accessorKey: 'riotId', header: 'Riot ID' },
  { accessorKey: 'platformId', header: 'Region' },
  { accessorKey: 'status', header: 'Status' },
  { accessorKey: 'score', header: 'Score' },
  { accessorKey: 'discoveredAtUtc', header: 'Discovered' },
  { accessorKey: 'validatedAtUtc', header: 'Validated' },
]

// =============================================================================
// View 2 — Demandes d'ajout (seed requests)
// =============================================================================
const seedStatuses: SeedRequestStatus[] = ['Pending', 'Resolving', 'Ingested', 'Failed']

const SEED_STATUS_COLOR: Record<SeedRequestStatus, BadgeColor> = {
  Pending: 'neutral',
  Resolving: 'info',
  Ingested: 'success',
  Failed: 'error',
}
const SEED_STATUS_ICON: Record<SeedRequestStatus, string> = {
  Pending: 'i-lucide-clock',
  Resolving: 'i-lucide-loader',
  Ingested: 'i-lucide-circle-check',
  Failed: 'i-lucide-circle-x',
}
function seedStatusColor(status: SeedRequestStatus): BadgeColor {
  return SEED_STATUS_COLOR[status] ?? 'neutral'
}
function seedStatusIcon(status: SeedRequestStatus): string {
  return SEED_STATUS_ICON[status] ?? 'i-lucide-circle'
}

const seedStatusItems = [
  { label: 'All statuses', value: ALL },
  ...seedStatuses.map(status => ({ label: status, value: status })),
]

const seedStatus = ref<'all' | SeedRequestStatus>(ALL)
const seedSearch = ref('')
const seedSearchDebounced = refDebounced(seedSearch, 300)

const seedFilters = computed(() => ({
  status: seedStatus.value === ALL ? undefined : seedStatus.value,
  search: seedSearchDebounced.value.trim() || undefined,
}))

const hasSeedFilters = computed(() =>
  seedStatus.value !== ALL || Boolean(seedSearch.value.trim()),
)
function resetSeedFilters() {
  seedStatus.value = ALL
  seedSearch.value = ''
}

const {
  data: seedData,
  pending: seedPending,
  error: seedError,
  refresh: refreshSeedRequests,
} = useSeedRequests(seedFilters)

const seedRows = computed<SeedRequestReadModel[]>(() => seedData.value ?? [])

const seedColumns: TableColumn<SeedRequestReadModel>[] = [
  { accessorKey: 'riotId', header: 'Riot ID' },
  { accessorKey: 'platformId', header: 'Region' },
  { accessorKey: 'status', header: 'Status' },
  { accessorKey: 'requestedAtUtc', header: 'Requested' },
  { accessorKey: 'processedAtUtc', header: 'Processed' },
  { accessorKey: 'error', header: 'Error' },
]

// --- Refresh both panels at once --------------------------------------------
const anyPending = computed(() => candidatePending.value || seedPending.value)
function refreshAll() {
  refreshCandidates()
  refreshSeedRequests()
}

// =============================================================================
// Candidate detail slide-over (Data Quality pattern: imperative + deep-linkable)
// =============================================================================
const {
  detailOpen,
  detail,
  detailPending,
  detailError,
  openDetail,
} = useDeepLinkedDetail<CandidateDetail>({
  queryKey: 'candidate',
  fetch: getCandidateDetail,
  notFoundMessage: id => `No candidate found with id "${id}".`,
  loadErrorMessage: 'Failed to load candidate detail.',
})

const detailTitle = computed(() =>
  detail.value
    ? riotIdLabel(detail.value.gameName, detail.value.tagLine)
    : 'Candidate detail',
)

// Ordered pipeline stages for the detail stepper, with the timestamp that marks
// each one. Processing has no dedicated timestamp on the entity, so it inherits
// the discovered floor for ordering only.
const PIPELINE_ORDER: MainCandidateStatus[] = [
  'New',
  'Scored',
  'Queued',
  'Processing',
  'Validated',
]
function isRejected(status: MainCandidateStatus | undefined): boolean {
  return status === 'Rejected'
}
</script>

<template>
  <UDashboardPanel id="candidates">
    <template #header>
      <UDashboardNavbar title="Candidates" icon="i-lucide-users-round">
        <template #leading>
          <UDashboardSidebarCollapse />
        </template>
        <template #right>
          <UButton
            icon="i-lucide-refresh-cw"
            color="neutral"
            variant="ghost"
            :loading="anyPending"
            aria-label="Refresh"
            @click="refreshAll()"
          />
        </template>
      </UDashboardNavbar>
    </template>

    <template #body>
      <!-- ============================ View 1 ============================= -->
      <UCard :ui="{ body: 'p-0 sm:p-0' }" class="mb-8">
        <template #header>
          <div class="flex flex-col gap-3">
            <div class="flex items-center justify-between gap-2">
              <div>
                <p class="text-sm font-medium text-highlighted">
                  Candidates
                </p>
                <p class="text-xs text-dimmed mt-0.5">
                  Main-candidate ingestion pipeline · New → Scored → Queued →
                  Processing → Validated.
                </p>
              </div>
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

        <UAlert
          v-if="candidateError"
          color="error"
          variant="subtle"
          icon="i-lucide-triangle-alert"
          title="Failed to load candidates"
          :description="candidateError.message"
          class="m-4"
        />

        <UTable
          :data="candidateRows"
          :columns="candidateColumns"
          :loading="candidatePending"
          loading-color="primary"
          :ui="{ tr: 'cursor-pointer hover:bg-elevated/40', td: 'py-2' }"
          @select="(_event, row) => openDetail(row.original.id)"
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
              {{ riotIdLabel(row.original.gameName, row.original.tagLine) }}
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

      <!-- ============================ View 2 ============================= -->
      <UCard :ui="{ body: 'p-0 sm:p-0' }">
        <template #header>
          <div class="flex flex-col gap-3">
            <div class="flex items-center justify-between gap-2">
              <div>
                <p class="text-sm font-medium text-highlighted">
                  Demandes d'ajout
                </p>
                <p class="text-xs text-dimmed mt-0.5">
                  Manual "add a main" requests · Pending → Resolving → Ingested /
                  Failed.
                </p>
              </div>
              <UBadge
                v-if="!seedPending"
                color="neutral"
                variant="subtle"
                :label="`${formatNumber(seedRows.length)} shown`"
              />
            </div>

            <!-- Seed-request filters -->
            <div class="flex flex-wrap items-center gap-2">
              <UInput
                v-model="seedSearch"
                icon="i-lucide-search"
                placeholder="Search Riot ID (gameName / tagLine)"
                class="w-full sm:w-80"
                :loading="seedPending"
              />
              <USelect
                v-model="seedStatus"
                :items="seedStatusItems"
                icon="i-lucide-filter"
                placeholder="Status"
                class="w-44"
              />
              <UButton
                v-if="hasSeedFilters"
                icon="i-lucide-x"
                color="neutral"
                variant="ghost"
                label="Clear"
                @click="resetSeedFilters"
              />
            </div>
          </div>
        </template>

        <UAlert
          v-if="seedError"
          color="error"
          variant="subtle"
          icon="i-lucide-triangle-alert"
          title="Failed to load seed requests"
          :description="seedError.message"
          class="m-4"
        />

        <UTable
          :data="seedRows"
          :columns="seedColumns"
          :loading="seedPending"
          loading-color="primary"
          :ui="{ td: 'py-2' }"
        >
          <template #riotId-cell="{ row }">
            <span class="text-sm text-default">
              {{ riotIdLabel(row.original.gameName, row.original.tagLine) }}
            </span>
          </template>
          <template #platformId-cell="{ row }">
            <span class="font-mono text-xs text-muted">{{ row.original.platformId }}</span>
          </template>
          <template #status-cell="{ row }">
            <UBadge
              :color="seedStatusColor(row.original.status)"
              variant="subtle"
              size="sm"
              :icon="seedStatusIcon(row.original.status)"
              :label="row.original.status"
            />
          </template>
          <template #requestedAtUtc-cell="{ row }">
            <span class="tabular-nums text-xs text-muted">
              {{ formatDateTime(row.original.requestedAtUtc) }}
            </span>
          </template>
          <template #processedAtUtc-cell="{ row }">
            <span class="tabular-nums text-xs text-muted">
              {{ formatDateTime(row.original.processedAtUtc) }}
            </span>
          </template>
          <template #error-cell="{ row }">
            <span
              v-if="row.original.error"
              class="text-xs text-error line-clamp-2"
              :title="row.original.error"
            >
              {{ row.original.error }}
            </span>
            <span v-else class="text-dimmed">—</span>
          </template>

          <template #empty>
            <div class="py-10 text-center text-sm text-muted">
              No add requests match these filters.
            </div>
          </template>
        </UTable>
      </UCard>

      <!-- Candidate detail slide-over -->
      <USlideover
        v-model:open="detailOpen"
        :title="detailTitle"
        :ui="{ content: 'sm:max-w-xl' }"
      >
        <template #body>
          <div v-if="detailPending" class="space-y-4">
            <USkeleton class="h-16 w-full" />
            <USkeleton class="h-48 w-full" />
          </div>

          <UAlert
            v-else-if="detailError"
            color="error"
            variant="subtle"
            icon="i-lucide-triangle-alert"
            title="Could not load candidate"
            :description="detailError"
          />

          <div v-else-if="detail" class="space-y-6">
            <!-- Identity header -->
            <div class="flex items-center gap-3">
              <NuxtImg
                v-if="iconFor(detail.championId)"
                :src="iconFor(detail.championId)!"
                :alt="nameFor(detail.championId)"
                width="40"
                height="40"
                loading="lazy"
                class="size-10 rounded-lg ring-1 ring-default"
              />
              <div v-else class="size-10 rounded-lg bg-elevated ring-1 ring-default" />
              <div class="min-w-0">
                <p class="text-sm font-medium text-highlighted truncate">
                  {{ nameFor(detail.championId) }}
                </p>
                <p class="text-xs text-muted truncate">
                  {{ riotIdLabel(detail.gameName, detail.tagLine) }}
                </p>
              </div>
              <UBadge
                :color="candidateStatusColor(detail.status)"
                variant="subtle"
                size="sm"
                :icon="candidateStatusIcon(detail.status)"
                :label="detail.status"
                class="ml-auto shrink-0"
              />
            </div>

            <!-- Pipeline stage stepper -->
            <div>
              <p class="text-muted text-xs uppercase mb-2">Pipeline stage</p>
              <UAlert
                v-if="isRejected(detail.status)"
                color="error"
                variant="subtle"
                icon="i-lucide-circle-x"
                title="Rejected"
                description="This candidate was ruled out of the pipeline (not a main)."
              />
              <ol v-else class="flex flex-wrap items-center gap-1.5">
                <li
                  v-for="stage in PIPELINE_ORDER"
                  :key="stage"
                  class="flex items-center gap-1.5"
                >
                  <UBadge
                    :color="PIPELINE_ORDER.indexOf(stage) <= PIPELINE_ORDER.indexOf(detail.status)
                      ? candidateStatusColor(detail.status)
                      : 'neutral'"
                    :variant="PIPELINE_ORDER.indexOf(stage) <= PIPELINE_ORDER.indexOf(detail.status)
                      ? 'subtle'
                      : 'soft'"
                    size="sm"
                    :label="stage"
                  />
                  <UIcon
                    v-if="stage !== 'Validated'"
                    name="i-lucide-chevron-right"
                    class="size-3 text-dimmed"
                  />
                </li>
              </ol>
            </div>

            <!-- Facts -->
            <dl class="grid grid-cols-2 gap-x-4 gap-y-3 text-sm">
              <div>
                <dt class="text-muted text-xs uppercase mb-0.5">Region</dt>
                <dd class="font-mono text-xs">{{ detail.platformId }}</dd>
              </div>
              <div>
                <dt class="text-muted text-xs uppercase mb-0.5">Score</dt>
                <dd class="tabular-nums">{{ detail.score.toFixed(3) }}</dd>
              </div>
              <div>
                <dt class="text-muted text-xs uppercase mb-0.5">Mastery points</dt>
                <dd class="tabular-nums">{{ formatNumber(detail.championPoints) }}</dd>
              </div>
              <div>
                <dt class="text-muted text-xs uppercase mb-0.5">Mastery rank</dt>
                <dd class="tabular-nums">#{{ detail.championRankInMasteryTop }}</dd>
              </div>
              <div>
                <dt class="text-muted text-xs uppercase mb-0.5">Ingested matches</dt>
                <dd class="tabular-nums">{{ formatNumber(detail.ingestedMatchCount) }}</dd>
              </div>
              <div>
                <dt class="text-muted text-xs uppercase mb-0.5">Last played</dt>
                <dd class="tabular-nums text-xs">{{ formatDateTime(detail.lastPlayTimeUtc) }}</dd>
              </div>
              <div>
                <dt class="text-muted text-xs uppercase mb-0.5">Discovered</dt>
                <dd class="tabular-nums text-xs">{{ formatDateTime(detail.discoveredAtUtc) }}</dd>
              </div>
              <div>
                <dt class="text-muted text-xs uppercase mb-0.5">Scored</dt>
                <dd class="tabular-nums text-xs">{{ formatDateTime(detail.scoredAtUtc) }}</dd>
              </div>
              <div>
                <dt class="text-muted text-xs uppercase mb-0.5">Validated</dt>
                <dd class="tabular-nums text-xs">{{ formatDateTime(detail.validatedAtUtc) }}</dd>
              </div>
              <div class="col-span-2">
                <dt class="text-muted text-xs uppercase mb-0.5">PUUID</dt>
                <dd class="font-mono text-xs break-all text-muted">{{ detail.puuid }}</dd>
              </div>
            </dl>

            <!-- Linked manual seed request -->
            <div>
              <p class="text-muted text-xs uppercase mb-2">Manual add request</p>
              <div
                v-if="detail.seedRequest"
                class="rounded-lg border border-default p-3 space-y-2"
              >
                <div class="flex items-center justify-between gap-2">
                  <span class="text-sm text-default">
                    {{ riotIdLabel(detail.seedRequest.gameName, detail.seedRequest.tagLine) }}
                  </span>
                  <UBadge
                    :color="seedStatusColor(detail.seedRequest.status)"
                    variant="subtle"
                    size="sm"
                    :icon="seedStatusIcon(detail.seedRequest.status)"
                    :label="detail.seedRequest.status"
                  />
                </div>
                <dl class="grid grid-cols-2 gap-x-4 gap-y-1.5 text-xs">
                  <div>
                    <dt class="text-muted uppercase">Requested</dt>
                    <dd class="tabular-nums">{{ formatDateTime(detail.seedRequest.requestedAtUtc) }}</dd>
                  </div>
                  <div>
                    <dt class="text-muted uppercase">Processed</dt>
                    <dd class="tabular-nums">{{ formatDateTime(detail.seedRequest.processedAtUtc) }}</dd>
                  </div>
                </dl>
                <UAlert
                  v-if="detail.seedRequest.error"
                  color="error"
                  variant="subtle"
                  size="sm"
                  icon="i-lucide-triangle-alert"
                  :description="detail.seedRequest.error"
                />
              </div>
              <p v-else class="text-sm text-muted">
                Discovered organically by the ladder — no manual request.
              </p>
            </div>
          </div>
        </template>
      </USlideover>
    </template>
  </UDashboardPanel>
</template>
