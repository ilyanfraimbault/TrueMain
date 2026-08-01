<script setup lang="ts">
// One automated detector (#924) as a *line*, not a card (#992).
//
// The card version printed everything a detector knows — headline, count label,
// every drill-down row, both configured thresholds and the source note — on
// every detector at once, so five healthy checks filled the screen as densely as
// five broken ones and the page had no shape. Here the line carries the verdict
// and the sentence explaining it; the reference material (thresholds, source,
// healthy rows) is one click away and nothing is lost.
//
// Deliberately generic, as before: the backend words every detector, so adding
// the next one stays a backend-only change and this component never branches on
// `key`.
import type { DataQualityDetector, DataQualityThreshold, DetectorStatus } from '~~/shared/types/ops'

const props = defineProps<{
  detector: DataQualityDetector
  /** Rendered on the expand when the detector offers a heavier breakdown. */
  drillDownLabel?: string
}>()

const emit = defineEmits<{ drillDown: [] }>()

const expanded = ref(false)

// One dot, one colour, one word — the status is stated once. `unknown` is
// neutral: not a pass and not an alarm, it says "not measured", and dressing it
// as either would be the dashboard lying.
const STATUS_META: Record<DetectorStatus, { dot: string, text: string, label: string }> = {
  green: { dot: 'bg-success', text: 'text-success', label: 'Passing' },
  amber: { dot: 'bg-warning', text: 'text-warning', label: 'Needs attention' },
  red: { dot: 'bg-error', text: 'text-error', label: 'Failing' },
  // A literal neutral rather than a semantic background token: `bg-muted` is a
  // surface colour and a 8px dot painted in it is invisible against the card,
  // which would leave an unmeasured check looking like a check with no verdict.
  unknown: { dot: 'bg-neutral-400 dark:bg-neutral-500', text: 'text-dimmed', label: 'Not measured' },
}

const meta = computed(() => STATUS_META[props.detector.status] ?? STATUS_META.unknown)

// Rows worth surfacing without a click: anything not green. A clean detector
// collapses to its headline, so the list reads as a list of problems rather than
// a wall of green rows.
const notableRows = computed(() => props.detector.rows.filter(row => row.status !== 'green'))
const visibleRows = computed(() => (expanded.value ? props.detector.rows : notableRows.value))

// Colour on a value means "this reading is off". A healthy row inside the
// expanded list stays neutral so the expand doesn't repaint the page.
function rowValueClass(status: DetectorStatus): string {
  return status === 'green' ? 'text-muted' : STATUS_META[status]?.text ?? 'text-dimmed'
}

function formatLevel(value: number, unit: DataQualityThreshold['unit']): string {
  // A ratio is a share of a baseline (0.4 = "40% of the median patch"), so print
  // it as one — a bare "0.4" next to counts and hours reads as a count.
  if (unit === 'ratio' || unit === 'percent') {
    const printed = unit === 'ratio' ? value * 100 : value
    return `${printed.toLocaleString('en-US', { maximumFractionDigits: 1 })}%`
  }
  const printed = value.toLocaleString('en-US', { maximumFractionDigits: 2 })
  return unit === 'hours' ? `${printed} h` : printed
}

/**
 * The configured line, in words. Stated rather than colour-coded: an amber and a
 * red number printed in amber and red put warning colours on a healthy card,
 * where the colour described a constant from the config file instead of the
 * state of the database.
 */
function describeThreshold(threshold: DataQualityThreshold): string {
  const side = threshold.direction === 'below' ? 'below' : 'above'
  const levels: string[] = []
  if (threshold.amber !== null) {
    levels.push(`warning ${side} ${formatLevel(threshold.amber, threshold.unit)}`)
  }
  if (threshold.red !== null) {
    levels.push(`alert ${side} ${formatLevel(threshold.red, threshold.unit)}`)
  }
  return levels.length > 0
    ? `${threshold.label} — ${levels.join(', ')}`
    : `${threshold.label} — no level configured`
}
</script>

<template>
  <div class="py-3">
    <!-- Collapsed line: verdict, name, and the sentence that explains it. -->
    <button
      type="button"
      class="group flex w-full items-start gap-3 text-left"
      :aria-expanded="expanded"
      @click="expanded = !expanded"
    >
      <span
        class="mt-1.5 size-2 shrink-0 rounded-full"
        :class="meta.dot"
      />
      <span class="min-w-0 flex-1">
        <span class="flex items-baseline gap-2">
          <span class="text-sm font-medium text-highlighted">{{ detector.title }}</span>
          <span class="sr-only">— {{ meta.label }}</span>
        </span>
        <span class="mt-0.5 block text-xs text-muted">{{ detector.headline }}</span>
        <!-- An unmeasured detector explains itself without a click: the reason IS
             the finding, and hiding it leaves a grey dot with no story. -->
        <span
          v-if="detector.unknownReason"
          class="mt-1 block text-xs text-dimmed italic"
        >{{ detector.unknownReason }}</span>
      </span>
      <UIcon
        :name="expanded ? 'i-lucide-chevron-up' : 'i-lucide-chevron-down'"
        class="mt-0.5 size-4 shrink-0 text-dimmed transition-colors group-hover:text-muted"
      />
    </button>

    <!-- Failing rows stay visible; healthy ones only appear on the expand. -->
    <ul v-if="visibleRows.length > 0" class="mt-2 space-y-1 ps-5">
      <li
        v-for="row in visibleRows"
        :key="row.label"
        class="flex items-start justify-between gap-3"
      >
        <span class="min-w-0">
          <span class="block font-mono text-xs text-highlighted">{{ row.label }}</span>
          <span v-if="row.note" class="block text-xs text-dimmed">{{ row.note }}</span>
        </span>
        <span
          class="text-xs whitespace-nowrap tabular-nums"
          :class="rowValueClass(row.status)"
        >{{ row.valueLabel ?? 'not measured' }}</span>
      </li>
    </ul>

    <!-- The reference material: what was counted, the lines it was judged
         against, and where the numbers come from. -->
    <div v-if="expanded" class="mt-3 space-y-2 ps-5">
      <p v-if="detector.count !== null" class="text-xs text-muted">
        {{ detector.count.toLocaleString('en-US') }} {{ detector.countLabel }}
      </p>
      <ul v-if="detector.thresholds.length > 0" class="space-y-0.5">
        <li
          v-for="threshold in detector.thresholds"
          :key="threshold.label"
          class="text-xs text-dimmed"
        >
          {{ describeThreshold(threshold) }}
        </li>
      </ul>

      <p class="text-xs text-dimmed">
        {{ detector.sourceNote }}
      </p>

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
  </div>
</template>
