<script setup lang="ts">
// "Candidates by state, over the window" — the funnel's LEVEL (#1403), drawn on the
// Pipeline tab's throughput card under the flow charts. Split out of
// AccountsPipeline.vue when it gained its second unit (#1534).
//
// Flow and stock answer different questions and neither derives from the other — a
// period that promotes everything it scores leaves the level flat, and so does a
// pipeline that has stopped — but they share an x axis, so they share the card's
// selectors too: one granularity, one window, one grid.
//
// Two units, and the chart says which one it is drawing (#1534). A candidate row is a
// (platform, puuid, champion) tuple, so a row count moves whenever the number of rows
// per account does: when the queue cap (#1361) started demoting all but each account's
// best champion, Validated rows fell ~4x overnight while the accounts validated per day
// did not move, and the curve read as if intake had stopped. Accounts answer "is intake
// still moving"; rows are what the queue actually holds. The rows-per-account line under
// the chart is what tells the two kinds of change apart.
import type {
  CandidateFunnelBucket,
  CandidateStock,
  CandidateStockAccounts,
  IngestionTimeGranularity,
} from '~~/shared/types/ops'
import { formatNumber } from '~~/shared/utils/format'

const props = defineProps<{
  /** The funnel's contiguous buckets — the x grid (see `chartData`). */
  funnelBuckets: CandidateFunnelBucket[]
  granularity: IngestionTimeGranularity
  stock: CandidateStock | null | undefined
  pending: boolean
  error: unknown
  /** Accounts demoted over the window, from the funnel's flow series. */
  demotedTotal: number
}>()

type Unit = 'rows' | 'accounts'
const unitItems: { label: string, value: Unit }[] = [
  { label: 'Rows', value: 'rows' },
  { label: 'Accounts', value: 'accounts' },
]
const unit = ref<Unit>('rows')

type Level = Omit<CandidateStockAccounts, 'rejected'>

/** One bucket's level in the selected unit; undefined when it was not measured in it. */
function levelOf(bucket: CandidateStock['buckets'][number] | undefined): Level | undefined {
  if (!bucket) {
    return undefined
  }
  return unit.value === 'rows' ? bucket : bucket.accounts ?? undefined
}

// Keyed by bucket so the chart can look a period up while iterating the funnel's own
// buckets. A period the snapshot missed is simply absent from this map.
const stockByBucket = computed(
  () => new Map((props.stock?.buckets ?? []).map(bucket => [bucket.bucket, bucket])),
)

/** The most recent reading in the window, for the figures under the chart. */
const latest = computed(() => props.stock?.buckets.at(-1) ?? null)
const latestLevel = computed(() => levelOf(latest.value ?? undefined))

// The x grid is the funnel's own buckets, which the backend zero-fills contiguously
// from the earliest run. That is deliberate: the snapshot series is sparse (a period
// the ingestor did not run has no reading at all), and looking each period up rather
// than plotting the readings back to back is what leaves a gap where an outage was.
// `undefined`, never 0 — the level then was unmeasured, not empty (#924). The same
// holds in accounts for every reading taken before that figure was recorded.
//
// `demoted` is not the same kind of number and the caption says so: it accumulates
// from the left edge of the selected window, so switching 7/30/90 days rescales that
// curve alone. It counts accounts in either unit — that is what the summaries record.
const chartData = computed(() => {
  const buckets = props.funnelBuckets
  const demoted = runningTotal(buckets.map(bucket => bucket.demoted))
  return buckets.map((bucket, index) => {
    const level = levelOf(stockByBucket.value.get(bucket.bucket))
    return {
      label: formatBucketLabel(bucket.bucket, props.granularity),
      new: level?.new,
      scored: level?.scored,
      queued: level?.queued,
      processing: level?.processing,
      validated: level?.validated,
      demoted: demoted[index] ?? undefined,
    }
  })
})
// No explicit colours: <ChartsAreaChart> assigns `CHART_SERIES` by declaration index,
// so funnel order IS slot order and the two cannot drift apart. Past slot three the
// palette's "legible whatever the order" property is gone (`chart-palette.ts`), so
// adjacency is load-bearing, and the values under the chart are what carry identity.
const chartCategories = {
  new: { name: 'New' },
  scored: { name: 'Scored' },
  queued: { name: 'Queued' },
  processing: { name: 'Processing' },
  validated: { name: 'Validated' },
  demoted: { name: 'Demoted accounts (cumulative)' },
}
const xFormatter = computed(() => indexLabelFormatter(chartData.value, row => row.label))

const unitNoun = computed(() => (unit.value === 'rows' ? 'candidate rows' : 'distinct accounts'))

/** Rows per account for one status, or null when either side cannot carry a ratio. */
function rowsPerAccount(key: keyof Level): string | null {
  const reading = latest.value
  const accounts = reading?.accounts?.[key]
  if (!reading || !accounts) {
    return null
  }
  return (reading[key] / accounts).toFixed(1)
}
const ratioLine = computed(() => {
  const parts = (['scored', 'queued', 'validated'] as const)
    .map(key => [key, rowsPerAccount(key)] as const)
    .filter(([, ratio]) => ratio !== null)
    .map(([key, ratio]) => `${ratio} ${key}`)
  return parts.length > 0 ? `Rows per account: ${parts.join(' · ')}` : null
})
</script>

<template>
  <div>
    <div class="flex items-start justify-between gap-4 mb-1.5">
      <PanelTitle
        variant="label"
        :title="`Candidates by state, over the window — in ${unitNoun}`"
      >
        <template #info>
          <p>
            <em>Rows</em> count candidates, one per (account, champion): an account
            with five candidate champions is five rows. <em>Accounts</em> count
            distinct (platform, PUUID) pairs. A row curve moves when throughput
            changes <em>or</em> when rows per account change — the queue cap keeps
            one champion per account, so it lowers rows without touching accounts.
            Compare the two, or read the rows-per-account line, before calling a
            drop a stall. Accounts are not disjoint across states (one champion
            validated and another queued counts in both), and readings taken before
            accounts were recorded show no accounts curve.
          </p>
          <p>
            The five statuses are levels — the last reading of each period,
            summed across platforms, never the sum of a period's readings. A
            period the ingestor did not run has no reading and breaks the curve
            rather than dropping it to zero. <em>Demoted</em> is the odd one out:
            a running total of accounts that restarts at the left edge of the
            window, so switching 7/30/90 days rescales that curve alone.
          </p>
          <p>
            New and Processing sit at 0 whenever the pipeline is healthy —
            scoring drains its whole backlog each run, and a claim lasts one
            ingestion pass. Sustained above zero they mean scoring is behind, or
            leases are not being reaped. A level is measured going forward from
            the first snapshot and never backfilled.
          </p>
        </template>
      </PanelTitle>
      <USelect
        v-model="unit"
        :items="unitItems"
        class="w-32"
        aria-label="Candidate level unit"
      />
    </div>
    <ChartsAreaChart
      :data="chartData"
      :height="240"
      :categories="chartCategories"
      :hide-area="true"
      :x-num-ticks="Math.min(chartData.length, 8)"
      :x-formatter="xFormatter"
      :y-formatter="formatCount"
    />
    <FetchErrorAlert
      v-if="error"
      :error="error"
      class="mt-3"
      title="Failed to load the candidate levels"
    />
    <template v-else-if="latest">
      <p v-if="latestLevel" class="mt-3 text-xs text-dimmed tabular-nums">
        Latest reading, in {{ unitNoun }}:
        {{ formatNumber(latestLevel.new) }} new ·
        {{ formatNumber(latestLevel.scored) }} scored ·
        {{ formatNumber(latestLevel.queued) }} queued ·
        {{ formatNumber(latestLevel.processing) }} processing ·
        {{ formatNumber(latestLevel.validated) }} validated ·
        {{ formatNumber(demotedTotal) }} accounts demoted over the window
      </p>
      <p v-else class="mt-3 text-xs text-dimmed">
        The latest reading predates the accounts count — switch to rows.
      </p>
      <p v-if="ratioLine" class="mt-1 text-xs text-dimmed tabular-nums">
        {{ ratioLine }}
      </p>
    </template>
    <p v-else-if="!pending" class="mt-3 text-xs text-dimmed">
      No candidate level on record in this window.
    </p>
  </div>
</template>
