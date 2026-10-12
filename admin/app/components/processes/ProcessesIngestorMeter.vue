<script setup lang="ts">
// The Ingestor's own meter (#1636), from `GET /api/ops/ingestor-metrics`: run failures,
// Riot rate-limit waits and 429s, each instrument split by tag set. Drawn generically —
// one block per instrument the payload carries — so a new instrument on the meter shows
// up here without a frontend change. An instrument absent from the payload recorded
// nothing in the window; with no rollup retained at all, nothing was ever measured and
// the card says so instead of reading as a quiet window (#924).
import type { IngestorInstrument, IngestorMetrics, IngestorMeterSeries } from '~~/shared/types/ops'
import { formatDateTime, formatNumber } from '~~/shared/utils/format'

const props = defineProps<{ metrics: IngestorMetrics }>()

// The window started before the first rollup: only part of it was measured.
const measuredSince = computed(() => {
  const oldest = props.metrics.oldestRetainedUtc
  return oldest && Date.parse(oldest) > Date.parse(props.metrics.sinceUtc) ? oldest : null
})

// `{failure}` is an OpenTelemetry annotation, not a unit a reader needs braces around.
function unitLabel(unit: string | null): string {
  return unit ? unit.replace(/[{}]/g, '') : ''
}

function amount(value: number, unit: string | null): string {
  const label = unitLabel(unit)
  const rounded = Math.abs(value) >= 10 ? Math.round(value) : Math.round(value * 10) / 10
  return label === 'ms' ? `${formatNumber(rounded)} ms` : formatNumber(rounded)
}

function headline(instrument: IngestorInstrument): string {
  if (instrument.kind === 'histogram') {
    const mean = instrument.count > 0 ? instrument.sum / instrument.count : 0
    return `${formatNumber(instrument.count)} recorded · mean ${amount(mean, instrument.unit)} · max ${amount(instrument.max, instrument.unit)}`
  }
  const label = unitLabel(instrument.unit)
  return `${formatNumber(instrument.sum)}${label ? ` ${label}${instrument.sum === 1 ? '' : 's'}` : ''}`
}

function seriesLabel(series: IngestorMeterSeries): string {
  const entries = Object.entries(series.tags).sort(([a], [b]) => a.localeCompare(b))
  return entries.length === 0 ? '(no tags)' : entries.map(([key, value]) => `${key}=${value}`).join(' · ')
}
</script>

<template>
  <UCard class="mb-6">
    <template #header>
      <PanelTitle
        title="Ingestor meter"
        subtitle="Run failures, rate-limit waits and 429s, as the Ingestor recorded them"
        info="The Ingestor's TrueMain.Ingestor meter, folded per minute by a MeterListener in the Ingestor and written to Mongo (meter_rollups). Counters show their total; histograms the number of measurements, their mean and their maximum. Rate-limit waits are recorded only when a call actually waited for a permit. An instrument missing below recorded nothing in the window."
      />
    </template>

    <p v-if="!metrics.oldestRetainedUtc" class="text-sm text-muted">
      Nothing measured yet: the Ingestor has written no meter rollup.
    </p>
    <template v-else>
      <p v-if="measuredSince" class="mb-4 text-xs text-muted">
        Measured since {{ formatDateTime(measuredSince) }} — the window starts before the first rollup.
      </p>
      <p v-if="metrics.instruments.length === 0" class="text-sm text-muted">
        No instrument recorded anything in this window.
      </p>
      <div class="flex flex-col gap-6">
        <section v-for="instrument in metrics.instruments" :key="instrument.name">
          <div class="flex flex-wrap items-baseline justify-between gap-2">
            <span class="font-mono text-sm text-highlighted">{{ instrument.name }}</span>
            <span class="tabular-nums text-sm text-highlighted">{{ headline(instrument) }}</span>
          </div>
          <p v-if="instrument.description" class="mt-0.5 text-xs text-muted">
            {{ instrument.description }}
          </p>
          <div class="mt-2 overflow-x-auto">
            <table class="w-full text-xs tabular-nums">
              <thead class="text-muted">
                <tr class="text-left">
                  <th class="py-1 pr-3 font-normal">
                    Series
                  </th>
                  <th class="py-1 pr-3 text-right font-normal">
                    {{ instrument.kind === 'histogram' ? 'Recorded' : 'Total' }}
                  </th>
                  <template v-if="instrument.kind === 'histogram'">
                    <th class="py-1 pr-3 text-right font-normal">
                      Mean
                    </th>
                    <th class="py-1 pr-3 text-right font-normal">
                      Max
                    </th>
                  </template>
                  <th class="py-1 text-right font-normal">
                    Last
                  </th>
                </tr>
              </thead>
              <tbody>
                <tr
                  v-for="series in instrument.series"
                  :key="seriesLabel(series)"
                  class="border-t border-default"
                >
                  <td class="py-1 pr-3 font-mono break-all">
                    {{ seriesLabel(series) }}
                  </td>
                  <td class="py-1 pr-3 text-right">
                    {{ formatNumber(instrument.kind === 'histogram' ? series.count : series.sum) }}
                  </td>
                  <template v-if="instrument.kind === 'histogram'">
                    <td class="py-1 pr-3 text-right">
                      {{ amount(series.mean, instrument.unit) }}
                    </td>
                    <td class="py-1 pr-3 text-right">
                      {{ amount(series.max, instrument.unit) }}
                    </td>
                  </template>
                  <td class="py-1 text-right text-muted whitespace-nowrap">
                    {{ formatDateTime(series.lastRecordedAtUtc) }}
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </section>
      </div>
    </template>
  </UCard>
</template>
