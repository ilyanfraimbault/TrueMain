<script setup lang="ts">
// Import OTP — two sections.
//
// A) Bulk paste (functional): paste Riot IDs one per line as `gameName#tagLine`,
//    optionally `gameName#tagLine,REGION`. Lines without an explicit region use
//    the default-region select. Parsing dedupes identical entries, flags
//    malformed lines, and renders a preview table with counts. "Seed all (N)"
//    POSTs every valid row to `POST /api/ops/accounts/seed` with limited
//    concurrency, tracking per-row status (queued/ok/failed) + a progress bar +
//    a final summary. Partial failure is surfaced honestly per row.
//
// B) Import from an external source (PENDING): a disabled card. Automated import
//    from external OTP-list sites is planned but the source connector is not
//    built — NO scraping, NO fabricated data. It will reuse the same seed
//    endpoint once a source is confirmed.
import type { TableColumn } from '@nuxt/ui'
import type { SeedRequestStatus } from '~~/shared/types/ops'

const TRACKED_REGIONS = ['EUW1', 'KR', 'NA1'] as const
type TrackedRegion = (typeof TRACKED_REGIONS)[number]

// --- Input -------------------------------------------------------------------
const raw = ref('')
const defaultRegion = ref<TrackedRegion>('EUW1')

const regionItems = TRACKED_REGIONS.map(r => ({ label: r, value: r }))

// Per-row seeding outcome, kept separate from the parsed-row identity so a
// re-parse (editing the textarea) doesn't wipe an in-flight/finished run until
// the user actually changes the rows.
type RowOutcome = 'pending' | 'queued' | 'ok' | 'failed'

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
const failedCount = computed(() =>
  Object.values(outcomes.value).filter(o => o.outcome === 'failed').length,
)
const hasRun = computed(() => okCount.value > 0 || failedCount.value > 0)

const progressPercent = computed(() => {
  const total = validRows.value.length
  return total === 0 ? 0 : Math.round((doneCount.value / total) * 100)
})

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

const CONCURRENCY = 3
const toast = useToast()

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
        outcomes.value[row.key] = { outcome: 'ok', status: res.status, error: null }
      }
      catch (err: unknown) {
        outcomes.value[row.key] = { outcome: 'failed', status: null, error: extractError(err) }
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
    const ok = okCount.value
    const failed = failedCount.value
    toast.add({
      title: failed === 0 ? 'All rows queued' : 'Import finished with errors',
      description: `${ok} queued · ${failed} failed`,
      color: failed === 0 ? 'success' : 'warning',
      icon: failed === 0 ? 'i-lucide-circle-check' : 'i-lucide-triangle-alert',
    })
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
const columns: TableColumn<PreviewRow>[] = [
  { accessorKey: 'gameName', header: 'Game name' },
  { accessorKey: 'tagLine', header: 'Tag' },
  { accessorKey: 'region', header: 'Region' },
  { accessorKey: 'outcome', header: 'Status' },
]

const tableMeta = {
  class: {
    tr: (row: { original: PreviewRow }) =>
      !row.original.valid || row.original.outcome === 'failed' ? 'bg-error/5' : '',
  },
}

type BadgeColor = 'neutral' | 'info' | 'success' | 'error' | 'warning'
function outcomeBadge(row: PreviewRow): { color: BadgeColor, icon: string, label: string } {
  if (!row.valid) {
    return { color: 'error', icon: 'i-lucide-circle-x', label: 'Invalid' }
  }
  switch (row.outcome) {
    case 'ok':
      return { color: 'success', icon: 'i-lucide-circle-check', label: 'Queued' }
    case 'failed':
      return { color: 'error', icon: 'i-lucide-circle-x', label: 'Failed' }
    case 'queued':
      return { color: 'info', icon: 'i-lucide-loader', label: 'Sending…' }
    default:
      return { color: 'neutral', icon: 'i-lucide-circle-dashed', label: 'Ready' }
  }
}
</script>

<template>
  <UDashboardPanel id="import">
    <template #header>
      <UDashboardNavbar title="Import OTP" icon="i-lucide-list-plus">
        <template #leading>
          <UDashboardSidebarCollapse />
        </template>
      </UDashboardNavbar>
    </template>

    <template #body>
      <!-- Section A: Bulk paste -->
      <section class="max-w-4xl">
        <div class="flex items-center gap-2 mb-1">
          <UIcon name="i-lucide-clipboard-list" class="size-4 text-primary" />
          <h2 class="text-sm font-medium text-highlighted">
            Bulk paste
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
                :items="regionItems"
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
          :color="failedCount === 0 ? 'success' : 'warning'"
          variant="subtle"
          :icon="failedCount === 0 ? 'i-lucide-circle-check' : 'i-lucide-triangle-alert'"
          :title="failedCount === 0 ? 'All rows queued' : 'Finished with errors'"
          :description="`${okCount} queued · ${failedCount} failed. Matches & mains follow on the next ingestion run.`"
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
            :columns="columns"
            :meta="tableMeta"
            class="max-h-[480px]"
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

      <!-- Section B: External source (pending) -->
      <section class="max-w-4xl mt-10">
        <div class="flex items-center gap-2 mb-3">
          <UIcon name="i-lucide-globe" class="size-4 text-muted" />
          <h2 class="text-sm font-medium text-highlighted">
            Import from an external source
          </h2>
          <UBadge color="neutral" variant="subtle" size="sm" label="Coming soon" />
        </div>

        <div class="rounded-lg border border-dashed border-default p-5 bg-elevated/10">
          <div class="flex items-start gap-3">
            <div class="p-2 rounded-lg bg-elevated/50 ring-1 ring-default shrink-0">
              <UIcon name="i-lucide-cloud-download" class="size-5 text-dimmed" />
            </div>
            <div class="min-w-0">
              <p class="text-sm text-default">
                Automated import of the top OTPs per champion per region from an
                external OTP-list source is planned, but the source connector is
                not built yet.
              </p>
              <p class="text-xs text-muted mt-1">
                No source is wired up, so there is nothing to pull and no data is
                shown here. Once a source is confirmed it will reuse the same
                seed endpoint as the bulk paste above.
              </p>
              <ul class="mt-3 space-y-1.5 text-xs text-muted">
                <li class="flex items-start gap-2">
                  <UIcon name="i-lucide-dot" class="size-4 shrink-0 text-dimmed" />
                  Pick a region and a per-champion top-N count.
                </li>
                <li class="flex items-start gap-2">
                  <UIcon name="i-lucide-dot" class="size-4 shrink-0 text-dimmed" />
                  Fetch the candidate Riot IDs from the configured source.
                </li>
                <li class="flex items-start gap-2">
                  <UIcon name="i-lucide-dot" class="size-4 shrink-0 text-dimmed" />
                  Review them in the same preview table, then seed in bulk.
                </li>
              </ul>
              <div class="mt-4">
                <UButton
                  icon="i-lucide-cloud-download"
                  color="neutral"
                  variant="subtle"
                  label="Fetch from source"
                  disabled
                />
              </div>
            </div>
          </div>
        </div>
      </section>
    </template>
  </UDashboardPanel>
</template>
