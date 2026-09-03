<script setup lang="ts">
import { Orientation } from '@unovis/ts'
import { computed } from 'vue'
import type { BulletLegendItemInterface } from 'vue-chrts/types'

// The tooltip body for every chart in the portal — <ChartsAreaChart> renders it
// when the call site gives no `#tooltip` slot of its own, <ChartsBarChart>
// always does (see that wrapper for why it cannot use upstream's).
//
// It replaces vue-chrts' built-in `Tooltip.vue`, and matches it where the
// semantics matter — the title falls back to the hovered row's first property,
// the rows are the categories the datum carries a value for — while dropping
// the part that did not survive contact with a dark surface: the inline
// `--vis-tooltip-*` fallbacks that painted a light card. The container those
// variables style is neutralised in `main.css`, so the card is drawn here, with
// the app's own tokens, and looks like every other elevated surface in the
// portal (#1404).
//
// The markup is rendered into a hidden <div> whose `innerHTML` @unovis/ts
// copies into its own tooltip container. Utility classes survive that copy —
// the stylesheet is global — but scoped styles and Vue event listeners would
// not, so this component stays presentational.

const props = defineProps<{
  /** The hovered datum, exactly as vue-chrts hands it to the `#tooltip` slot. */
  values: unknown
  categories: Record<string, BulletLegendItemInterface>
  orientation?: Orientation
  xFormatter?: (value: number | Date) => string
  yFormatter?: (value: number | Date) => string
  /**
   * Builds the tooltip title from the hovered row. `any` is deliberate, not
   * laziness: function parameters are contravariant under `strictFunctionTypes`,
   * so widening this to `Record<string, unknown>` makes the shared
   * `labelTooltipTitle(d: { label: string })` unassignable — measured at 12
   * errors, one per call site.
   */
  titleFormatter?: (row: any) => string | number
}>()

/**
 * The hovered row. Stacked bars arrive wrapped by @unovis/ts as
 * `{ datum, index, stacked, stackIndex, isEnding }`; grouped bars, horizontal
 * bars and area series arrive as the row itself. Keyed on the presence of
 * `datum` rather than on the chart's `stacked` prop, so it keeps working if
 * upstream ever normalises the two.
 */
const row = computed<Record<string, unknown> | null>(() => {
  const values = props.values
  if (!values || typeof values !== 'object') {
    return null
  }
  const datum = (values as { datum?: unknown }).datum
  return (datum && typeof datum === 'object' ? datum : values) as Record<string, unknown>
})

/**
 * Which formatter turns a series value into text. @unovis/ts maps the VALUE to
 * the bottom (x) axis when the bars run horizontally and to the left (y) axis
 * when they run vertically, so the two orientations format a value with opposite
 * props — a horizontal chart's `yFormatter` is its index -> label lookup, not its
 * value formatter. Upstream makes the same swap; getting it wrong here prints a
 * bucket label where a count belongs. Area charts are always vertical.
 */
const valueFormatter = computed(() =>
  props.orientation === Orientation.Horizontal ? props.xFormatter : props.yFormatter,
)

const title = computed(() => {
  const current = row.value
  if (!current) {
    return ''
  }
  if (props.titleFormatter) {
    return String(props.titleFormatter(current))
  }
  const first = Object.values(current)[0]
  return first === undefined ? '' : String(first)
})

/** One row per category the hovered datum actually carries a number for. */
const series = computed(() => {
  const current = row.value
  if (!current) {
    return []
  }
  return Object.entries(props.categories)
    .map(([key, category]) => ({ key, category, value: current[key] }))
    .filter(entry => typeof entry.value === 'number' && Number.isFinite(entry.value))
    .map(entry => ({
      key: entry.key,
      name: entry.category.name ?? entry.key,
      color: Array.isArray(entry.category.color) ? entry.category.color[0] : entry.category.color,
      text: valueFormatter.value
        ? valueFormatter.value(entry.value as number)
        : String(entry.value),
    }))
})
</script>

<template>
  <div
    v-if="row"
    class="rounded-md border border-default bg-elevated px-2.5 py-2 text-xs shadow-md"
  >
    <p class="font-semibold text-default">
      {{ title }}
    </p>
    <div
      v-if="series.length"
      class="mt-1.5 grid grid-cols-[auto_1fr_auto] items-center gap-x-2 gap-y-1"
    >
      <template v-for="entry in series" :key="entry.key">
        <span
          class="size-2 shrink-0 rounded-full"
          :style="{ backgroundColor: entry.color }"
        />
        <span class="whitespace-nowrap text-muted">{{ entry.name }}</span>
        <span class="text-right font-semibold tabular-nums text-default">{{ entry.text }}</span>
      </template>
    </div>
  </div>
</template>
