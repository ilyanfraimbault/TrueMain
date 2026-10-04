// Run state of the "Add in bulk" form on the Accounts hub's "Add mains" tab
// (split out of `AccountsSeed.vue`, #1436): the pasted list, its parse, and a
// "seed all" that POSTs every valid row with limited concurrency, tracking
// per-row outcomes, progress and a summary. Parsing itself is in
// `utils/seed-bulk.ts`.
import type { SeedRequestStatus } from '~~/shared/types/ops'
import type { TrackedRegion } from '~~/shared/utils/regions'
import type { BulkParsedRow, BulkPreviewRow, BulkRowOutcome } from '~/utils/seed-bulk'
import { parseBulkSeedLines } from '~/utils/seed-bulk'

const CONCURRENCY = 3

interface RowResult { outcome: BulkRowOutcome, status: SeedRequestStatus | null, error: string | null }

/** `onFinished` runs once a "seed all" settles, so the caller can surface the new requests. */
export function useBulkSeed(onFinished: () => void) {
  const actionToast = useActionToast()

  const raw = ref('')
  const defaultRegion = ref<TrackedRegion>('EUW1')

  const parsedRows = computed<BulkParsedRow[]>(() => parseBulkSeedLines(raw.value, defaultRegion.value))
  const validRows = computed(() => parsedRows.value.filter(r => r.valid))
  const invalidCount = computed(() => parsedRows.value.length - validRows.value.length)

  // Outcomes keyed by row `key`, so they survive incidental re-parses and map
  // cleanly onto the preview. Reset whenever the set of valid rows changes.
  const outcomes = ref<Record<string, RowResult>>({})
  const running = ref(false)
  const doneCount = ref(0)

  const previewRows = computed<BulkPreviewRow[]>(() =>
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

  async function seedAll() {
    const rows = validRows.value
    if (running.value || rows.length === 0) {
      return
    }

    running.value = true
    doneCount.value = 0
    // Initialize every valid row to "queued" (in-flight) so the table reads as
    // pending work immediately, then flip per-row as each request settles.
    const next: Record<string, RowResult> = {}
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
      // A toast, and legitimately so: a bulk run can finish while the operator is
      // reading another panel, and its outcome is otherwise only in the summary
      // alert further down the page (#1661).
      if (summaryClean.value) actionToast.success(summaryTitle.value, summaryDescription.value)
      else actionToast.warning(summaryTitle.value, summaryDescription.value)
      // Surface the newly-queued rows at the top of the queue list.
      onFinished()
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

  return {
    raw,
    defaultRegion,
    parsedRows,
    validRows,
    invalidCount,
    previewRows,
    running,
    doneCount,
    hasRun,
    summaryClean,
    summaryTitle,
    summaryDescription,
    progressPercent,
    seedAll,
    clearAll,
  }
}
