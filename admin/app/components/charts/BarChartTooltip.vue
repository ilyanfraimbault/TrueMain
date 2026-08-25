<script setup lang="ts">
import { Orientation } from '@unovis/ts'
import { computed } from 'vue'
import type { BulletLegendItemInterface } from 'vue-chrts/types'

// The tooltip body for <ChartsBarChart>. Split out of the wrapper so the hovered
// row and its series are resolved ONCE per render as computeds, rather than
// recomputed by every expression that needs them in the template.
//
// It renders into a hidden <div> whose `innerHTML` @unovis/ts copies into its
// own tooltip container, so the styling below is deliberately inline and keyed
// on upstream's CSS variables: this markup has to be indistinguishable from the
// tooltip an area chart draws. See the wrapper for why it exists at all.

const props = defineProps<{
  /** The hovered datum, exactly as vue-chrts hands it to the `#tooltip` slot. */
  values: unknown
  categories: Record<string, BulletLegendItemInterface>
  orientation?: Orientation
  xFormatter?: (value: number | Date) => string
  yFormatter?: (value: number | Date) => string
  titleFormatter?: (row: any) => string | number
}>()

/**
 * The hovered row. Stacked bars arrive wrapped by @unovis/ts as
 * `{ datum, index, stacked, stackIndex, isEnding }`; grouped and horizontal bars
 * arrive as the row itself. Keyed on the presence of `datum` rather than on the
 * chart's `stacked` prop, so it keeps working if upstream ever normalises the two.
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
 * bucket label where a count belongs.
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
  <div v-if="row" style="display: flex; flex-direction: column;">
    <div
      :style="{
        color: 'var(--vis-tooltip-title-color, #000)',
        borderBottom: series.length
          ? 'var(--vis-tooltip-title-border-bottom, 1px solid #e5e7eb)'
          : 'none',
        padding: 'var(--vis-tooltip-title-padding, 0.75rem 0.75rem 0.5rem 0.75rem)',
        margin: series.length ? 'var(--vis-tooltip-title-margin, 0 0 0.25rem 0)' : '0',
        fontSize: 'var(--vis-tooltip-title-font-size, 0.875rem)',
        lineHeight: 'var(--vis-tooltip-title-line-height, 100%)',
        fontWeight: 'var(--vis-tooltip-title-font-weight, 600)',
      }"
    >
      {{ title }}
    </div>
    <div
      v-if="series.length"
      style="display: grid; grid-template-columns: auto 1fr auto; align-items: center;
             gap: var(--vis-tooltip-content-gap, 0.25rem 0.5rem);
             padding: var(--vis-tooltip-content-padding, 0 0.75rem 0.5rem 0.75rem);"
    >
      <template v-for="entry in series" :key="entry.key">
        <span
          :style="{
            width: '8px',
            height: '8px',
            aspectRatio: '1',
            borderRadius: 'var(--vis-tooltip-dot-border-radius, 4px)',
            flexShrink: '0',
            backgroundColor: entry.color,
          }"
        />
        <span
          style="font-weight: var(--vis-tooltip-label-font-weight, 400);
                 font-size: var(--vis-tooltip-label-font-size, 0.875rem);
                 color: var(--vis-tooltip-label-color, inherit);
                 margin: var(--vis-tooltip-label-margin, 0 1rem 0 0);
                 white-space: nowrap;"
        >{{ entry.name }}</span>
        <span
          style="font-size: var(--vis-tooltip-value-font-size, 0.875rem);
                 font-weight: var(--vis-tooltip-value-font-weight, 600);
                 color: var(--vis-tooltip-value-color, inherit);
                 text-align: right; font-variant-numeric: tabular-nums;"
        >{{ entry.text }}</span>
      </template>
    </div>
  </div>
</template>
