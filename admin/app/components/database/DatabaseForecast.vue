<script setup lang="ts">
// Database panel: the disk forecast (#925), the reason the panel keeps history at
// all. Reads the page's `GET /api/ops/db/history` payload.
import type { DbStorageHistory } from '~~/shared/types/ops'
import { formatDate, humanizeBytes } from '~~/shared/utils/format'

const props = defineProps<{
  history: DbStorageHistory | null | undefined
  pending: boolean
}>()

const dailyPoints = computed(() => props.history?.daily ?? [])
const forecast = computed(() => props.history?.forecast ?? null)

// What the numbers actually cover. Postgres and Mongo share one volume, so the
// disk figures sum both — but only once both have been measured, and Mongo is
// optional in every environment. Saying so is the difference between "the disk
// is 60 GB" and "the part of the disk we measured is 60 GB".
const enginesCovered = computed(() => props.history?.engines ?? [])
const coverageLabel = computed(() => {
  const engines = enginesCovered.value
  if (engines.length === 0) {
    return null
  }
  return engines.map(storageEngineLabel).join(' + ')
})
const latestPoint = computed(() => dailyPoints.value.at(-1) ?? null)

// How many trailing days the backend is willing to fit — days measuring the same
// engines as the latest one. Read from the payload rather than re-derived here:
// a second implementation of the rule would drift, and the drift would show up as
// the panel confidently naming the wrong reason.
const comparableDays = computed(() => props.history?.comparableDays ?? 0)

// Why there is no forecast, in the operator's terms. The backend deliberately
// returns null rather than a placeholder date, so the panel has to say which
// reason applies instead of rendering an empty card.
const MIN_FORECAST_DAYS = 3

const forecastAbsenceReason = computed(() => {
  if (props.pending || forecast.value) {
    return null
  }
  if (dailyPoints.value.length === 0) {
    return 'No snapshots recorded yet — the ingestor writes one per pipeline run.'
  }
  if (comparableDays.value < MIN_FORECAST_DAYS) {
    // Fewer comparable days than charted days means the set of measured engines
    // changed recently: the newcomer's footprint lands in one step, and a step is
    // not a growth rate, so the fit restarts after it.
    return comparableDays.value < dailyPoints.value.length
      ? `The measured engines changed ${comparableDays.value} day(s) ago — the trend restarts `
        + `from there, and needs ${MIN_FORECAST_DAYS} days covering the same engines.`
      : `Only ${dailyPoints.value.length} day(s) of history — ${MIN_FORECAST_DAYS} are needed `
        + 'before a trend can be fitted.'
  }
  return 'Storage is flat or shrinking over this window, or no disk capacity is configured (StorageHistory:DiskCapacityBytes).'
})

function crossingLabel(projectedAtUtc: string | null): string {
  if (projectedAtUtc === null) {
    return 'No date at this rate'
  }
  const date = new Date(projectedAtUtc)
  const label = formatDate(projectedAtUtc)
  return date.getTime() < Date.now() ? `Already exceeded (${label})` : label
}

function crossingColor(projectedAtUtc: string | null): 'error' | 'warning' | 'neutral' {
  if (projectedAtUtc === null) {
    return 'neutral'
  }
  const days = (new Date(projectedAtUtc).getTime() - Date.now()) / 86_400_000
  // A month is roughly the lead time needed to resize a volume without drama.
  return days < 0 ? 'error' : days < 30 ? 'warning' : 'neutral'
}
</script>

<template>
  <UCard class="mb-6">
    <template #header>
      <div class="flex items-center justify-between gap-2">
        <PanelTitle variant="label" title="Disk forecast">
          <!-- Never let the figures pass as "the disk" without saying what
               they add up: Mongo is optional, and before its first snapshot
               the totals are Postgres alone. -->
          <template v-if="coverageLabel" #subtitle>
            Covering {{ coverageLabel }}<template v-if="latestPoint && latestPoint.mongoBytes > 0">
              — {{ humanizeBytes(latestPoint.postgresBytes, 1) }} + {{ humanizeBytes(latestPoint.mongoBytes, 1) }}
            </template>
          </template>
        </PanelTitle>
        <UBadge
          v-if="forecast"
          color="neutral"
          variant="subtle"
          :label="`+${humanizeBytes(forecast.bytesPerDay, 1)}/day`"
        />
      </div>
    </template>

    <USkeleton v-if="pending" class="h-16 w-full" />
    <p v-else-if="forecastAbsenceReason" class="text-sm text-muted">
      {{ forecastAbsenceReason }}
    </p>
    <div v-else-if="forecast" class="flex flex-wrap gap-3">
      <div
        v-for="crossing in forecast.crossings"
        :key="crossing.percent"
        class="flex min-w-[12rem] flex-col gap-1 rounded-lg border border-default px-3 py-2"
      >
        <p class="text-xs text-muted">
          {{ crossing.percent }}% of {{ humanizeBytes(forecast.diskCapacityBytes, 0) }}
          ({{ humanizeBytes(crossing.thresholdBytes, 0) }})
        </p>
        <UBadge
          :color="crossingColor(crossing.projectedAtUtc)"
          variant="subtle"
          :label="crossingLabel(crossing.projectedAtUtc)"
        />
      </div>
    </div>
  </UCard>
</template>
