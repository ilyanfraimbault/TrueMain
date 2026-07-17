<script setup lang="ts">
// Add mains — register Riot IDs for ingestion via `POST /api/ops/accounts/seed`.
// This single page covers BOTH ways to add (they hit the same endpoint):
//
//   1. Single add — the 3-field form (gameName, tagLine, region). On submit we
//      get a request id back and POLL `GET /api/ops/accounts/seed/{id}` every
//      ~2s, driving a live status stepper (Pending -> Resolving ->
//      Ingested/Failed) until a terminal status or a ~30s timeout.
//   2. Bulk add — paste Riot IDs one per line as `gameName#tagLine` (optionally
//      `gameName#tagLine,REGION`). Parsing dedupes, flags malformed lines, and
//      renders a preview table; "Seed all (N)" POSTs every valid row with
//      limited concurrency, tracking per-row status + a progress bar + summary.
//
// A shared "Recent seed requests" history table at the bottom reflects requests
// from BOTH the single and bulk flows (each submit calls `refresh()`).
//
// WORDING NOTE: "Ingested"/"queued" here means the account + mastery-derived
// candidates were created and queued — actual match ingestion + main
// classification happen on the NEXT Ingestor cycle, not synchronously.
import type { FormError, FormSubmitEvent, TableColumn } from '@nuxt/ui'
import type {
  BadgeColor,
  SeedRequestReadModel,
  SeedRequestStatus,
} from '~~/shared/types/ops'
import { TERMINAL_SEED_STATUSES } from '~~/shared/types/ops'
import { formatDateTime } from '~~/shared/utils/format'

// Tracked Riot regions, shared by the single form and the bulk parser.
const TRACKED_REGIONS = ['EUW1', 'KR', 'NA1'] as const
type TrackedRegion = (typeof TRACKED_REGIONS)[number]
// Region <select> options. `value` is widened to `string` so the single-add
// form (whose `state.region` is a free `string`) type-checks; the bulk
// `defaultRegion` select is additionally constrained to `TrackedRegion` via its
// own model ref, so an out-of-set value can't slip through there.
const regionItems: { label: string, value: string }[] = TRACKED_REGIONS.map(
  r => ({ label: r, value: r }),
)

const toast = useToast()

// =============================================================================
// 1) SINGLE ADD — 3-field form + live status polling
// =============================================================================
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
    submitError.value = extractFetchError(err)
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

// =============================================================================
// 2) BULK ADD — paste a list, preview, seed all with limited concurrency
// =============================================================================
const raw = ref('')
const defaultRegion = ref<TrackedRegion>('EUW1')
// Strictly-typed options for the bulk default-region select so its model stays
// constrained to a `TrackedRegion`.
const bulkRegionItems = TRACKED_REGIONS.map(r => ({ label: r, value: r }))

// Per-row seeding outcome, kept separate from the parsed-row identity so a
// re-parse (editing the textarea) doesn't wipe an in-flight/finished run until
// the user actually changes the rows.
// `duplicate` = the backend returned an existing (still-unprocessed) request
// instead of creating one, i.e. this account was already seeded. It's a soft
// outcome (no work lost), distinct from a hard `failed`.
type RowOutcome = 'pending' | 'queued' | 'ok' | 'duplicate' | 'failed'

interface ParsedRow {
  // Stable identity for dedupe + outcome tracking (lowercased triple).
  key: string
  lineNo: number
  gameName: string
  tagLine: string
  region: TrackedRegion
  valid: boolean
  reason: string | null
}

interface PreviewRow extends ParsedRow {
  outcome: RowOutcome
  /** Resulting status from the backend (e.g. Pending) when queued. */
  status: SeedRequestStatus | null
  error: string | null
}

// A line is `gameName#tagLine` with an optional `,REGION` suffix. Region tokens
// are matched case-insensitively against the tracked set; an unknown region is
// a hard error (we don't silently fall back, to avoid seeding the wrong shard).
function parseLine(line: string, lineNo: number, fallback: TrackedRegion): ParsedRow | null {
  const trimmed = line.trim()
  if (!trimmed) {
    return null // blank lines are ignored, not flagged
  }

  let body = trimmed
  let region: TrackedRegion = fallback
  let regionError: string | null = null

  const commaIdx = trimmed.lastIndexOf(',')
  if (commaIdx !== -1) {
    body = trimmed.slice(0, commaIdx).trim()
    const regionToken = trimmed.slice(commaIdx + 1).trim().toUpperCase()
    const match = TRACKED_REGIONS.find(r => r === regionToken)
    if (match) {
      region = match
    }
    else {
      regionError = `Unknown region "${trimmed.slice(commaIdx + 1).trim()}"`
    }
  }

  const hashIdx = body.indexOf('#')
  const gameName = (hashIdx === -1 ? body : body.slice(0, hashIdx)).trim()
  const tagLine = hashIdx === -1 ? '' : body.slice(hashIdx + 1).trim()

  let reason: string | null = regionError
  if (!reason) {
    if (hashIdx === -1) {
      reason = 'Missing "#tagLine"'
    }
    else if (!gameName) {
      reason = 'Missing game name'
    }
    else if (!tagLine) {
      reason = 'Missing tag line'
    }
  }

  return {
    key: `${gameName.toLowerCase()}#${tagLine.toLowerCase()}@${region}`,
    lineNo,
    gameName,
    tagLine,
    region,
    valid: reason === null,
    reason,
  }
}

// Parsed rows derived from the textarea. Duplicate valid entries (same
// name#tag@region) collapse to the first occurrence and the rest are flagged so
// the operator sees why a count dropped. Invalid lines are always kept.
const parsedRows = computed<ParsedRow[]>(() => {
  const lines = raw.value.split('\n')
  const seen = new Set<string>()
  const out: ParsedRow[] = []
  lines.forEach((line, idx) => {
    const row = parseLine(line, idx + 1, defaultRegion.value)
    if (!row) {
      return
    }
    if (row.valid) {
      if (seen.has(row.key)) {
        out.push({ ...row, valid: false, reason: 'Duplicate of an earlier line' })
        return
      }
      seen.add(row.key)
    }
    out.push(row)
  })
  return out
})

const validRows = computed(() => parsedRows.value.filter(r => r.valid))
const invalidCount = computed(() => parsedRows.value.length - validRows.value.length)

// --- Run state ---------------------------------------------------------------
// Outcomes keyed by row `key`, so they survive incidental re-parses and map
// cleanly onto the preview. Reset whenever the set of valid rows changes.
const outcomes = ref<Record<string, { outcome: RowOutcome, status: SeedRequestStatus | null, error: string | null }>>({})
const running = ref(false)
const doneCount = ref(0)

const previewRows = computed<PreviewRow[]>(() =>
  parsedRows.value.map((r) => {
    const o = outcomes.value[r.key]
    return {
      ...r,
      outcome: r.valid ? (o?.outcome ?? 'pending') : 'pending',
      status: o?.status ?? null,
      error: o?.error ?? r.reason,
    }
  }),
)

// Identity of the current valid set; when it changes we drop stale outcomes so
// the summary/progress never describe a run against rows that no longer exist.
const validSignature = computed(() => validRows.value.map(r => r.key).join('|'))
watch(validSignature, () => {
  if (running.value) {
    return
  }
  outcomes.value = {}
  doneCount.value = 0
})

const okCount = computed(() =>
  Object.values(outcomes.value).filter(o => o.outcome === 'ok').length,
)
const duplicateCount = computed(() =>
  Object.values(outcomes.value).filter(o => o.outcome === 'duplicate').length,
)
const failedCount = computed(() =>
  Object.values(outcomes.value).filter(o => o.outcome === 'failed').length,
)
const hasRun = computed(() => okCount.value > 0 || duplicateCount.value > 0 || failedCount.value > 0)

// Shared run summary, used by BOTH the completion toast and the persistent
// result banner so they never disagree. A clean run created every row; otherwise
// some were already seeded and/or failed.
const summaryClean = computed(() => failedCount.value === 0 && duplicateCount.value === 0)
const summaryTitle = computed(() =>
  failedCount.value > 0
    ? 'Import finished with errors'
    : duplicateCount.value > 0
      ? 'Some accounts were already seeded'
      : 'All rows queued',
)
// Only the non-zero buckets, so an all-already-seeded run reads "3 already
// seeded" rather than "0 queued · 3 already seeded".
const summaryDescription = computed(() => {
  const parts: string[] = []
  if (okCount.value > 0) {
    parts.push(`${okCount.value} queued`)
  }
  if (duplicateCount.value > 0) {
    parts.push(`${duplicateCount.value} already seeded`)
  }
  if (failedCount.value > 0) {
    parts.push(`${failedCount.value} failed`)
  }
  return parts.join(' · ')
})

const progressPercent = computed(() => {
  const total = validRows.value.length
  return total === 0 ? 0 : Math.round((doneCount.value / total) * 100)
})

const CONCURRENCY = 3

async function seedAll() {
  const rows = validRows.value
  if (running.value || rows.length === 0) {
    return
  }

  running.value = true
  doneCount.value = 0
  // Initialize every valid row to "queued" (in-flight) so the table reads as
  // pending work immediately, then flip per-row as each request settles.
  const next: typeof outcomes.value = {}
  for (const r of rows) {
    next[r.key] = { outcome: 'queued', status: null, error: null }
  }
  outcomes.value = next

  // Simple worker-pool: CONCURRENCY workers pull from a shared cursor so at most
  // N requests are in flight at once.
  let cursor = 0
  async function worker() {
    while (cursor < rows.length) {
      const row = rows[cursor++]
      if (!row) {
        break
      }
      try {
        const res = await seedAccount({
          gameName: row.gameName,
          tagLine: row.tagLine,
          platformId: row.region,
        })
        // `created === false` means the backend returned an existing request
        // for this Riot ID + platform — already seeded, nothing new queued.
        outcomes.value[row.key] = {
          outcome: res.created ? 'ok' : 'duplicate',
          status: res.status,
          error: null,
        }
      }
      catch (err: unknown) {
        outcomes.value[row.key] = { outcome: 'failed', status: null, error: extractFetchError(err) }
      }
      finally {
        doneCount.value += 1
      }
    }
  }

  try {
    await Promise.all(
      Array.from({ length: Math.min(CONCURRENCY, rows.length) }, () => worker()),
    )
  }
  finally {
    running.value = false
    toast.add({
      title: summaryTitle.value,
      description: summaryDescription.value,
      color: summaryClean.value ? 'success' : 'warning',
      icon: summaryClean.value ? 'i-lucide-circle-check' : 'i-lucide-triangle-alert',
    })
    // Surface the newly-queued rows in the shared history below.
    refresh()
  }
}

function clearAll() {
  if (running.value) {
    return
  }
  raw.value = ''
  outcomes.value = {}
  doneCount.value = 0
}

// --- Preview table -----------------------------------------------------------
const previewColumns: TableColumn<PreviewRow>[] = [
  { accessorKey: 'gameName', header: 'Game name' },
  { accessorKey: 'tagLine', header: 'Tag' },
  { accessorKey: 'region', header: 'Region' },
  { accessorKey: 'outcome', header: 'Status' },
]

const previewTableMeta = {
  class: {
    tr: (row: { original: PreviewRow }) => {
      if (!row.original.valid || row.original.outcome === 'failed') {
        return 'bg-error/5'
      }
      if (row.original.outcome === 'duplicate') {
        return 'bg-warning/5'
      }
      return ''
    },
  },
}

function outcomeBadge(row: PreviewRow): { color: BadgeColor, icon: string, label: string } {
  if (!row.valid) {
    return { color: 'error', icon: 'i-lucide-circle-x', label: 'Invalid' }
  }
  switch (row.outcome) {
    case 'ok':
      return { color: 'success', icon: 'i-lucide-circle-check', label: 'Queued' }
    case 'duplicate':
      return { color: 'warning', icon: 'i-lucide-circle-alert', label: 'Already seeded' }
    case 'failed':
      return { color: 'error', icon: 'i-lucide-circle-x', label: 'Failed' }
    case 'queued':
      return { color: 'info', icon: 'i-lucide-loader', label: 'Sending…' }
    default:
      return { color: 'neutral', icon: 'i-lucide-circle-dashed', label: 'Ready' }
  }
}

// =============================================================================
// SHARED — recent seed requests history (reflects single + bulk submits)
// =============================================================================
const statusFilter = ref<'all' | SeedRequestStatus>(ALL)
const statusFilterItems = [
  { label: 'All statuses', value: ALL },
  { label: 'Pending', value: 'Pending' },
  { label: 'Resolving', value: 'Resolving' },
  { label: 'Ingested', value: 'Ingested' },
  { label: 'Failed', value: 'Failed' },
]

const listFilters = computed(() => ({
  status: statusFilter.value === ALL ? undefined : statusFilter.value,
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
      <UDashboardNavbar title="Add mains" icon="i-lucide-user-plus">
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
      <!-- 1) Single add -->
      <section class="max-w-4xl">
        <div class="flex items-center gap-2 mb-1">
          <UIcon name="i-lucide-user-plus" class="size-4 text-primary" />
          <h2 class="text-sm font-medium text-highlighted">
            Add a single main
          </h2>
        </div>
        <p class="text-xs text-muted mb-4">
          Register one Riot ID and watch it resolve live.
        </p>

        <UForm
          :state="state"
          :validate="validate"
          class="space-y-4"
          @submit="onSubmit"
        >
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
      </section>

      <USeparator class="my-8" />

      <!-- 2) Bulk add — full-width so the preview table uses all available space.
           (The single-add form above stays narrow; this section drives a table.) -->
      <section>
        <div class="flex items-center gap-2 mb-1">
          <UIcon name="i-lucide-clipboard-list" class="size-4 text-primary" />
          <h2 class="text-sm font-medium text-highlighted">
            Add in bulk
          </h2>
        </div>
        <p class="text-xs text-muted mb-4">
          One Riot ID per line as <code class="font-mono text-default">gameName#tagLine</code>.
          Append <code class="font-mono text-default">,REGION</code> to override the default region per line.
        </p>

        <div class="grid grid-cols-1 lg:grid-cols-[1fr_auto] gap-3 items-start mb-3">
          <UTextarea
            v-model="raw"
            :rows="8"
            :disabled="running"
            autoresize
            :maxrows="16"
            placeholder="Faker#KR1&#10;Caps#EUW,EUW1&#10;Doublelift#NA1,NA1"
            class="w-full font-mono"
            :ui="{ base: 'font-mono text-sm' }"
          />
          <div class="flex flex-row lg:flex-col gap-2 lg:w-44">
            <UFormField label="Default region" class="w-full">
              <USelect
                v-model="defaultRegion"
                :items="bulkRegionItems"
                icon="i-lucide-globe"
                :disabled="running"
                class="w-full"
              />
            </UFormField>
          </div>
        </div>

        <!-- Counts -->
        <div class="flex flex-wrap items-center gap-2 mb-3">
          <UBadge
            color="success"
            variant="subtle"
            size="sm"
            :icon="'i-lucide-circle-check'"
            :label="`${validRows.length} valid`"
          />
          <UBadge
            v-if="invalidCount > 0"
            color="error"
            variant="subtle"
            size="sm"
            icon="i-lucide-circle-x"
            :label="`${invalidCount} skipped`"
          />
          <span v-if="parsedRows.length === 0" class="text-xs text-dimmed">
            Nothing parsed yet.
          </span>
        </div>

        <!-- Actions -->
        <div class="flex flex-wrap items-center gap-3 mb-4">
          <UButton
            icon="i-lucide-upload"
            :label="`Seed all (${validRows.length})`"
            :loading="running"
            :disabled="validRows.length === 0 || running"
            @click="seedAll"
          />
          <UButton
            icon="i-lucide-trash-2"
            color="neutral"
            variant="ghost"
            label="Clear"
            :disabled="running || raw.length === 0"
            @click="clearAll"
          />
        </div>

        <!-- Progress while running -->
        <div v-if="running" class="mb-4">
          <div class="flex items-center justify-between text-xs text-muted mb-1.5">
            <span>Seeding {{ doneCount }} / {{ validRows.length }}…</span>
            <span class="tabular-nums">{{ progressPercent }}%</span>
          </div>
          <UProgress :model-value="progressPercent" :max="100" color="primary" />
        </div>

        <!-- Final summary -->
        <UAlert
          v-else-if="hasRun"
          :color="summaryClean ? 'success' : 'warning'"
          variant="subtle"
          :icon="summaryClean ? 'i-lucide-circle-check' : 'i-lucide-triangle-alert'"
          :title="summaryTitle"
          :description="`${summaryDescription}. Matches & mains follow on the next ingestion run.`"
          class="mb-4"
        />

        <!-- Preview / result table -->
        <UCard v-if="previewRows.length > 0" :ui="{ body: 'p-0 sm:p-0' }">
          <template #header>
            <p class="text-sm font-medium text-highlighted">
              Preview
            </p>
          </template>

          <UTable
            :data="previewRows"
            :columns="previewColumns"
            :meta="previewTableMeta"
            :ui="{ td: 'py-2' }"
          >
            <template #gameName-cell="{ row }">
              <span class="font-medium text-highlighted">{{ row.original.gameName || '—' }}</span>
            </template>
            <template #tagLine-cell="{ row }">
              <span class="text-muted font-mono text-xs">
                {{ row.original.tagLine ? `#${row.original.tagLine}` : '—' }}
              </span>
            </template>
            <template #region-cell="{ row }">
              <span class="text-muted font-mono text-xs">{{ row.original.region }}</span>
            </template>
            <template #outcome-cell="{ row }">
              <div class="flex items-center gap-2">
                <UBadge
                  :color="outcomeBadge(row.original).color"
                  :icon="outcomeBadge(row.original).icon"
                  variant="subtle"
                  size="sm"
                  :label="outcomeBadge(row.original).label"
                  :ui="{ leadingIcon: row.original.outcome === 'queued' ? 'animate-spin' : '' }"
                />
                <span
                  v-if="row.original.error"
                  class="text-error text-xs line-clamp-1 max-w-xs"
                  :title="row.original.error"
                >
                  {{ row.original.error }}
                </span>
              </div>
            </template>

            <template #empty>
              <div class="py-10 text-center text-sm text-muted">
                Paste some Riot IDs above to preview them.
              </div>
            </template>
          </UTable>
        </UCard>
      </section>

      <USeparator class="my-8" />

      <!-- Shared: recent seed requests (single + bulk) -->
      <section>
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
      </section>
    </template>
  </UDashboardPanel>
</template>
