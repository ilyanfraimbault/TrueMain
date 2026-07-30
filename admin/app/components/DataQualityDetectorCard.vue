<script setup lang="ts">
// One automated detector (#924): its verdict, headline number, the line it drew,
// and the per-row drill-down. Deliberately generic — the backend words every
// detector, so adding the next one is a backend-only change and this component
// never branches on `key`.
import type { DataQualityDetector, DetectorStatus } from '~~/shared/types/ops'

const props = defineProps<{
  detector: DataQualityDetector
  /** Rendered under the rows when the detector offers a heavier breakdown. */
  drillDownLabel?: string
}>()

const emit = defineEmits<{ drillDown: [] }>()

const expanded = ref(false)

// Unknown is neutral, not a pass and not an alarm: it says "not measured", and
// dressing it as either would be the dashboard lying.
const STATUS_META: Record<DetectorStatus, { color: 'success' | 'warning' | 'error' | 'neutral', icon: string, label: string }> = {
  green: { color: 'success', icon: 'i-lucide-circle-check', label: 'OK' },
  amber: { color: 'warning', icon: 'i-lucide-triangle-alert', label: 'Warning' },
  red: { color: 'error', icon: 'i-lucide-octagon-alert', label: 'Alert' },
  unknown: { color: 'neutral', icon: 'i-lucide-circle-help', label: 'Unknown' },
}

const meta = computed(() => STATUS_META[props.detector.status] ?? STATUS_META.unknown)

// Rows worth surfacing without a click: anything not green. A clean detector
// collapses to its headline, so the panel reads as a list of problems rather
// than a wall of green rows.
const notableRows = computed(() => props.detector.rows.filter(row => row.status !== 'green'))
const visibleRows = computed(() => (expanded.value ? props.detector.rows : notableRows.value))
const hiddenRowCount = computed(() => props.detector.rows.length - notableRows.value.length)

function rowClass(status: DetectorStatus): string {
  return STATUS_META[status]?.color === 'error'
    ? 'text-error'
    : STATUS_META[status]?.color === 'warning'
      ? 'text-warning'
      : STATUS_META[status]?.color === 'success'
        ? 'text-success'
        : 'text-dimmed'
}

function formatThreshold(value: number | null, unit: string): string {
  if (value === null) {
    return '—'
  }
  // A ratio is a share of a baseline (0.4 = "below 40% of the median patch"), so
  // print it as one — a bare "0.4" next to counts and hours reads as a count.
  if (unit === 'ratio') {
    return `${(value * 100).toLocaleString('en-US', { maximumFractionDigits: 0 })}%`
  }
  const printed = value.toLocaleString('en-US', { maximumFractionDigits: 2 })
  return unit === 'percent'
    ? `${printed}%`
    : unit === 'hours'
      ? `${printed} h`
      : printed
}
</script>

<template>
  <UCard>
    <template #header>
      <div class="flex items-start justify-between gap-3">
        <div class="flex items-start gap-2.5 min-w-0">
          <UIcon
            :name="meta.icon"
            class="size-5 shrink-0 mt-0.5"
            :class="{
              'text-success': meta.color === 'success',
              'text-warning': meta.color === 'warning',
              'text-error': meta.color === 'error',
              'text-dimmed': meta.color === 'neutral',
            }"
          />
          <div class="min-w-0">
            <p class="text-sm font-medium text-highlighted">
              {{ detector.title }}
            </p>
            <p class="text-xs text-muted">
              {{ detector.headline }}
            </p>
          </div>
        </div>
        <UBadge
          :color="meta.color"
          variant="subtle"
          :label="detector.count !== null
            ? `${detector.count.toLocaleString('en-US')}`
            : meta.label"
        />
      </div>
    </template>

    <div class="space-y-3">
      <p v-if="detector.count !== null" class="text-xs text-dimmed">
        {{ detector.countLabel }}
      </p>

      <UAlert
        v-if="detector.unknownReason"
        color="neutral"
        variant="subtle"
        icon="i-lucide-circle-help"
        title="Not measured"
        :description="detector.unknownReason"
        :ui="{ description: 'text-xs' }"
      />

      <!-- Drill-down rows. Only the non-green ones by default. -->
      <ul v-if="visibleRows.length > 0" class="divide-default divide-y">
        <li
          v-for="row in visibleRows"
          :key="row.label"
          class="flex items-start justify-between gap-3 py-1.5"
        >
          <div class="min-w-0">
            <p class="text-xs text-highlighted font-mono">
              {{ row.label }}
            </p>
            <p v-if="row.note" class="text-xs text-dimmed">
              {{ row.note }}
            </p>
          </div>
          <span
            class="text-xs whitespace-nowrap tabular-nums"
            :class="rowClass(row.status)"
          >
            {{ row.valueLabel ?? 'not measured' }}
          </span>
        </li>
      </ul>

      <div class="flex flex-wrap items-center gap-2">
        <UButton
          v-if="hiddenRowCount > 0 || (expanded && detector.rows.length > 0)"
          size="xs"
          color="neutral"
          variant="ghost"
          :icon="expanded ? 'i-lucide-chevron-up' : 'i-lucide-chevron-down'"
          :label="expanded ? 'Hide healthy rows' : `Show ${hiddenRowCount} healthy row(s)`"
          @click="expanded = !expanded"
        />
        <UButton
          v-if="detector.hasDrillDownEndpoint"
          size="xs"
          color="neutral"
          variant="subtle"
          icon="i-lucide-search"
          :label="drillDownLabel ?? 'Break it down'"
          @click="emit('drillDown')"
        />
      </div>

      <!-- The configured lines, echoed: a colour alone never explains itself. -->
      <div v-if="detector.thresholds.length > 0" class="flex flex-wrap gap-x-4 gap-y-1 pt-1">
        <span
          v-for="threshold in detector.thresholds"
          :key="threshold.label"
          class="text-xs text-dimmed"
        >
          {{ threshold.label }}:
          <span class="text-warning">{{ formatThreshold(threshold.amber, threshold.unit) }}</span>
          /
          <span class="text-error">{{ formatThreshold(threshold.red, threshold.unit) }}</span>
        </span>
      </div>

      <p class="text-xs text-dimmed border-default border-t pt-2">
        {{ detector.sourceNote }}
      </p>
    </div>
  </UCard>
</template>
