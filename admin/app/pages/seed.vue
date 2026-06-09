<script setup lang="ts">
// Seed a main — register a Riot ID for ingestion via `POST /api/ops/accounts/seed`.
// On submit we get a request id back and POLL `GET /api/ops/accounts/seed/{id}`
// every ~2s, driving a live status stepper (Pending -> Resolving ->
// Ingested/Failed) until a terminal status or a ~30s timeout. A "Recent seed
// requests" table below lists prior submissions and refreshes after each submit.
//
// WORDING NOTE: "Ingested" here means the account + mastery-derived candidates
// were created and queued — actual match ingestion + main classification happen
// on the NEXT Ingestor cycle, not synchronously. The success copy says so.
import type { FormError, FormSubmitEvent, TableColumn } from '@nuxt/ui'
import type {
  SeedRequestReadModel,
  SeedRequestStatus,
} from '~~/shared/types/ops'
import { TERMINAL_SEED_STATUSES } from '~~/shared/types/ops'
import { formatDateTime } from '~~/shared/utils/format'

// --- Form --------------------------------------------------------------------
interface SeedFormState {
  gameName: string
  tagLine: string
  region: string
}

const state = reactive<SeedFormState>({
  gameName: '',
  tagLine: '',
  region: '',
})

const regionItems = [
  { label: 'EUW1', value: 'EUW1' },
  { label: 'KR', value: 'KR' },
  { label: 'NA1', value: 'NA1' },
]

// Normalize a tag line: drop a leading '#' (people paste `Name#TAG`) and trim.
function normalizeTag(raw: string): string {
  return raw.replace(/^#/, '').trim()
}

function validate(s: SeedFormState): FormError[] {
  const errors: FormError[] = []
  if (!s.gameName.trim()) {
    errors.push({ name: 'gameName', message: 'Required' })
  }
  if (!normalizeTag(s.tagLine)) {
    errors.push({ name: 'tagLine', message: 'Required' })
  }
  if (!s.region) {
    errors.push({ name: 'region', message: 'Pick a region' })
  }
  return errors
}

// --- Tracked request + polling ----------------------------------------------
const submitting = ref(false)
// The request currently being tracked (live status surfaces from this).
const tracked = ref<SeedRequestReadModel | null>(null)
// Set when the submit itself failed (network / 400) before we got an id.
const submitError = ref<string | null>(null)
// True while the poll loop is still running (non-terminal, pre-timeout).
const polling = ref(false)
// Flips true if we stop polling on the ~30s safety timeout rather than a
// terminal status — the UI then says "still processing" instead of overclaiming.
const polledOut = ref(false)

const POLL_INTERVAL_MS = 2000
const POLL_TIMEOUT_MS = 30000

let pollTimer: ReturnType<typeof setTimeout> | null = null
let pollDeadline = 0

function clearPoll() {
  if (pollTimer) {
    clearTimeout(pollTimer)
    pollTimer = null
  }
  polling.value = false
}

function isTerminal(status: SeedRequestStatus): boolean {
  return TERMINAL_SEED_STATUSES.includes(status)
}

async function pollOnce(id: string) {
  try {
    const next = await getSeedRequest(id)
    // A newer submit may have replaced the tracked request mid-flight; ignore
    // stale responses so we never resurrect an old poll's result.
    if (tracked.value?.id !== id) {
      return
    }
    tracked.value = next
    if (isTerminal(next.status)) {
      clearPoll()
      // The terminal request just landed — reflect it in the table too.
      refresh()
      return
    }
  }
  catch {
    // Transient poll error: keep the last known state and retry until the
    // deadline. A persistent failure simply ends at the timeout branch.
  }

  if (Date.now() >= pollDeadline) {
    polledOut.value = true
    clearPoll()
    return
  }
  pollTimer = setTimeout(() => pollOnce(id), POLL_INTERVAL_MS)
}

function startPolling(id: string) {
  clearPoll()
  polledOut.value = false
  // Already terminal (e.g. idempotent hit on an Ingested/Failed request)? Skip.
  if (tracked.value && isTerminal(tracked.value.status)) {
    refresh()
    return
  }
  polling.value = true
  pollDeadline = Date.now() + POLL_TIMEOUT_MS
  pollTimer = setTimeout(() => pollOnce(id), POLL_INTERVAL_MS)
}

const toast = useToast()

async function onSubmit(event: FormSubmitEvent<SeedFormState>) {
  submitError.value = null
  tracked.value = null
  polledOut.value = false
  clearPoll()
  submitting.value = true

  const body = {
    gameName: event.data.gameName.trim(),
    tagLine: normalizeTag(event.data.tagLine),
    platformId: event.data.region,
  }

  try {
    const res = await seedAccount(body)
    // Seed the tracked request with what we know now; polling fills the rest.
    tracked.value = {
      id: res.id,
      gameName: body.gameName,
      tagLine: body.tagLine,
      platformId: body.platformId,
      status: res.status,
      error: null,
      requestedAtUtc: new Date().toISOString(),
      processedAtUtc: null,
      resolvedPuuid: null,
      resolvedRiotAccountId: null,
    }
    refresh()
    startPolling(res.id)
  }
  catch (err: unknown) {
    submitError.value = extractError(err)
    toast.add({
      title: 'Seed request failed',
      description: submitError.value,
      color: 'error',
      icon: 'i-lucide-triangle-alert',
    })
  }
  finally {
    submitting.value = false
  }
}

// Pull a human message out of an ofetch error (400 body, then statusMessage).
function extractError(err: unknown): string {
  const e = err as {
    data?: { message?: string, statusMessage?: string }
    statusMessage?: string
    message?: string
  }
  return (
    e?.data?.message
    ?? e?.data?.statusMessage
    ?? e?.statusMessage
    ?? e?.message
    ?? 'Unexpected error'
  )
}

onBeforeUnmount(clearPoll)

const trackedRiotId = computed(() =>
  tracked.value ? `${tracked.value.gameName}#${tracked.value.tagLine}` : '',
)

// --- Status presentation -----------------------------------------------------
type StatusColor = 'neutral' | 'info' | 'success' | 'error'

function statusColor(s: SeedRequestStatus): StatusColor {
  switch (s) {
    case 'Ingested':
      return 'success'
    case 'Failed':
      return 'error'
    case 'Resolving':
      return 'info'
    default:
      return 'neutral'
  }
}
function statusIcon(s: SeedRequestStatus): string {
  switch (s) {
    case 'Ingested':
      return 'i-lucide-circle-check'
    case 'Failed':
      return 'i-lucide-circle-x'
    case 'Resolving':
      return 'i-lucide-loader'
    default:
      return 'i-lucide-clock'
  }
}

// Stepper model: Pending -> Resolving -> (Ingested | Failed). The active step
// derives from the tracked status; Failed marks the resolve step as errored.
const STEPS: { key: SeedRequestStatus, label: string }[] = [
  { key: 'Pending', label: 'Queued' },
  { key: 'Resolving', label: 'Resolving Riot ID' },
  { key: 'Ingested', label: 'Account queued' },
]
const STEP_RANK: Record<SeedRequestStatus, number> = {
  Pending: 0,
  Resolving: 1,
  Ingested: 2,
  // Failed shares the resolve stage visually (that's where it usually breaks).
  Failed: 1,
}
const activeRank = computed(() =>
  tracked.value ? STEP_RANK[tracked.value.status] : -1,
)
const isFailed = computed(() => tracked.value?.status === 'Failed')

// --- Recent seed requests table ----------------------------------------------
const statusFilter = ref<'' | SeedRequestStatus>('')
const statusFilterItems = [
  { label: 'All statuses', value: '' },
  { label: 'Pending', value: 'Pending' },
  { label: 'Resolving', value: 'Resolving' },
  { label: 'Ingested', value: 'Ingested' },
  { label: 'Failed', value: 'Failed' },
]

const listFilters = computed(() => ({
  status: statusFilter.value || undefined,
  limit: 50,
}))

const { data, pending, error, refresh } = useSeedRequests(listFilters)
const requests = computed(() => data.value ?? [])

const columns: TableColumn<SeedRequestReadModel>[] = [
  { accessorKey: 'gameName', header: 'Riot ID' },
  { accessorKey: 'platformId', header: 'Region' },
  { accessorKey: 'status', header: 'Status' },
  { accessorKey: 'requestedAtUtc', header: 'Requested' },
  { accessorKey: 'processedAtUtc', header: 'Processed' },
  { accessorKey: 'error', header: 'Error' },
]

const tableMeta = {
  class: {
    tr: (row: { original: SeedRequestReadModel }) =>
      row.original.status === 'Failed' ? 'bg-error/5' : '',
  },
}
</script>

<template>
  <UDashboardPanel id="seed">
    <template #header>
      <UDashboardNavbar title="Seed a main" icon="i-lucide-user-plus">
        <template #leading>
          <UDashboardSidebarCollapse />
        </template>
        <template #right>
          <UButton
            icon="i-lucide-refresh-cw"
            color="neutral"
            variant="ghost"
            :loading="pending"
            aria-label="Refresh requests"
            @click="refresh()"
          />
        </template>
      </UDashboardNavbar>
    </template>

    <template #body>
      <div class="max-w-2xl">
        <!-- Seed form -->
        <UForm
          :state="state"
          :validate="validate"
          class="space-y-4"
          @submit="onSubmit"
        >
          <p class="text-xs text-muted uppercase">
            Register a Riot ID
          </p>

          <div class="grid grid-cols-1 sm:grid-cols-[1fr_auto_10rem] gap-3 items-start">
            <UFormField label="Game name" name="gameName" required>
              <UInput
                v-model="state.gameName"
                placeholder="e.g. Faker"
                icon="i-lucide-user"
                class="w-full"
                autocomplete="off"
              />
            </UFormField>

            <UFormField label="Tag line" name="tagLine" required>
              <UInput
                v-model="state.tagLine"
                placeholder="KR1"
                class="w-full sm:w-28"
                autocomplete="off"
                :ui="{ leading: 'ps-2.5' }"
              >
                <template #leading>
                  <span class="text-dimmed text-sm">#</span>
                </template>
              </UInput>
            </UFormField>

            <UFormField label="Region" name="region" required>
              <USelect
                v-model="state.region"
                :items="regionItems"
                placeholder="Region"
                icon="i-lucide-globe"
                class="w-full"
              />
            </UFormField>
          </div>

          <div class="flex items-center gap-3">
            <UButton
              type="submit"
              icon="i-lucide-user-plus"
              label="Seed main"
              :loading="submitting"
            />
            <p class="text-xs text-dimmed">
              Tracked regions: EUW1 · KR · NA1
            </p>
          </div>
        </UForm>

        <!-- Submit-level error (network / 400 before an id was issued) -->
        <UAlert
          v-if="submitError"
          color="error"
          variant="subtle"
          icon="i-lucide-triangle-alert"
          title="Could not queue this Riot ID"
          :description="submitError"
          class="mt-6"
        />

        <!-- Live status of the tracked request -->
        <div
          v-if="tracked"
          class="mt-6 rounded-lg border p-4 bg-elevated/25"
          :class="isFailed ? 'border-error/30' : 'border-default'"
        >
          <div class="flex items-center justify-between gap-2 mb-4">
            <p class="font-medium text-highlighted truncate">
              {{ trackedRiotId }}
              <span class="text-muted font-normal">· {{ tracked.platformId }}</span>
            </p>
            <UBadge
              :color="statusColor(tracked.status)"
              :icon="statusIcon(tracked.status)"
              variant="subtle"
              size="sm"
              :label="tracked.status"
              :ui="{ leadingIcon: polling && tracked.status === 'Resolving' ? 'animate-spin' : '' }"
            />
          </div>

          <!-- Stepper -->
          <ol class="flex items-center gap-2">
            <template v-for="(step, i) in STEPS" :key="step.key">
              <li class="flex items-center gap-2 min-w-0">
                <span
                  class="flex items-center justify-center size-6 rounded-full text-xs shrink-0 ring-1"
                  :class="[
                    isFailed && i === 2
                      ? 'bg-error/10 text-error ring-error/30'
                      : i < activeRank || (i === 2 && tracked.status === 'Ingested')
                        ? 'bg-success/10 text-success ring-success/30'
                        : i === activeRank
                          ? 'bg-primary/10 text-primary ring-primary/30'
                          : 'bg-elevated text-dimmed ring-default',
                  ]"
                >
                  <UIcon
                    v-if="i < activeRank || (i === 2 && tracked.status === 'Ingested')"
                    name="i-lucide-check"
                    class="size-3.5"
                  />
                  <UIcon
                    v-else-if="i === activeRank && polling"
                    name="i-lucide-loader"
                    class="size-3.5 animate-spin"
                  />
                  <span v-else>{{ i + 1 }}</span>
                </span>
                <span
                  class="text-xs truncate"
                  :class="i <= activeRank ? 'text-default' : 'text-dimmed'"
                >
                  {{ step.label }}
                </span>
              </li>
              <li
                v-if="i < STEPS.length - 1"
                class="flex-1 h-px min-w-4"
                :class="i < activeRank ? 'bg-success/40' : 'bg-default'"
              />
            </template>
          </ol>

          <!-- Terminal / in-flight detail -->
          <div class="mt-4 text-sm">
            <UAlert
              v-if="tracked.status === 'Ingested'"
              color="success"
              variant="subtle"
              icon="i-lucide-circle-check"
              title="Account queued"
              description="Matches & mains will follow on the next ingestion run — only the account and its mastery-derived candidates have been created so far."
            />
            <UAlert
              v-else-if="tracked.status === 'Failed'"
              color="error"
              variant="subtle"
              icon="i-lucide-circle-x"
              title="Seeding failed"
              :description="tracked.error ?? 'The Riot ID could not be resolved.'"
            />
            <p v-else-if="polledOut" class="text-muted">
              Still processing after {{ POLL_TIMEOUT_MS / 1000 }}s — it will
              continue in the background. Use refresh to check on it later.
            </p>
            <p v-else class="text-muted flex items-center gap-2">
              <UIcon name="i-lucide-loader" class="size-4 animate-spin" />
              {{ tracked.status === 'Resolving'
                ? 'Resolving the Riot ID with the Riot API…'
                : 'Waiting for the resolver to pick this up…' }}
            </p>

            <!-- Resolved PUUID, once known -->
            <div
              v-if="tracked.resolvedPuuid"
              class="mt-3 flex items-baseline gap-2"
            >
              <span class="text-muted text-xs uppercase">PUUID</span>
              <code class="font-mono text-xs text-default break-all">
                {{ tracked.resolvedPuuid }}
              </code>
            </div>
          </div>
        </div>
      </div>

      <!-- Recent seed requests -->
      <div class="mt-8">
        <div class="flex items-center justify-between gap-2 mb-3">
          <p class="text-xs text-muted uppercase">
            Recent seed requests
          </p>
          <USelect
            v-model="statusFilter"
            :items="statusFilterItems"
            icon="i-lucide-check-circle"
            placeholder="Status"
            class="w-44"
          />
        </div>

        <UAlert
          v-if="error"
          color="error"
          variant="subtle"
          icon="i-lucide-triangle-alert"
          title="Failed to load seed requests"
          :description="error.message"
          class="mb-4"
        />

        <UCard :ui="{ body: 'p-0 sm:p-0' }">
          <template #header>
            <div class="flex items-center justify-between gap-2">
              <p class="text-sm font-medium text-highlighted">
                History
              </p>
              <UBadge
                v-if="!pending"
                color="neutral"
                variant="subtle"
                :label="`${requests.length} ${requests.length === 1 ? 'request' : 'requests'}`"
              />
            </div>
          </template>

          <UTable
            :data="requests"
            :columns="columns"
            :meta="tableMeta"
            :loading="pending"
            loading-color="primary"
            sticky
            class="max-h-[520px]"
            :ui="{ td: 'py-2' }"
          >
            <template #gameName-cell="{ row }">
              <span class="font-medium text-highlighted">
                {{ row.original.gameName }}<span class="text-dimmed">#{{ row.original.tagLine }}</span>
              </span>
            </template>
            <template #platformId-cell="{ row }">
              <span class="text-muted font-mono text-xs">
                {{ row.original.platformId }}
              </span>
            </template>
            <template #status-cell="{ row }">
              <UBadge
                :color="statusColor(row.original.status)"
                :icon="statusIcon(row.original.status)"
                variant="subtle"
                size="sm"
                :label="row.original.status"
              />
            </template>
            <template #requestedAtUtc-cell="{ row }">
              <span class="text-muted whitespace-nowrap">
                {{ formatDateTime(row.original.requestedAtUtc) }}
              </span>
            </template>
            <template #processedAtUtc-cell="{ row }">
              <span class="text-muted whitespace-nowrap">
                {{ formatDateTime(row.original.processedAtUtc) }}
              </span>
            </template>
            <template #error-cell="{ row }">
              <span
                v-if="row.original.error"
                class="text-error text-xs line-clamp-1 max-w-xs"
                :title="row.original.error"
              >
                {{ row.original.error }}
              </span>
              <span v-else class="text-dimmed">—</span>
            </template>

            <template #empty>
              <div class="py-10 text-center text-sm text-muted">
                No seed requests yet.
              </div>
            </template>
          </UTable>
        </UCard>
      </div>
    </template>
  </UDashboardPanel>
</template>
