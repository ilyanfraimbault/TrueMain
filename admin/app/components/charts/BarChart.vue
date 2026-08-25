<script setup lang="ts" generic="TItem extends Record<string, unknown>">
import { Orientation } from '@unovis/ts'
import { computed } from 'vue'
import type { BulletLegendItemInterface } from 'vue-chrts/types'

// Thin wrapper over <NcBarChart> that repairs two tooltip defects in vue-chrts.
// Both were verified against 2.1.4 in a browser and both are still present in
// 2.2.1, so upgrading is not the fix:
//
//   1. A STACKED bar chart never shows any values — the box opens empty. Each
//      stacked bar is bound by @unovis/ts to a wrapper object,
//      `{ datum, index, stacked, stackIndex, isEnding }`, but the upstream
//      tooltip looks the category keys up on that wrapper's ROOT and excludes
//      only the wrapper's OLD key names (`_index` / `_stacked` / `_ending`).
//      Nothing matches, so it renders neither title nor rows. Grouped and
//      horizontal bars bind the row itself, which is why they were unaffected.
//
//   2. The FIRST hover on any bar chart shows an empty box. The tooltip trigger
//      mutates a Vue ref and then reads the hidden slot wrapper's `innerHTML`
//      in the same tick — one frame before Vue has flushed that render. Any
//      second mousemove over the same bar fixes it, which is what made the bug
//      look intermittent rather than systematic.
//
// Repairing both here rather than at each call site: (1) by rendering the
// tooltip ourselves through the `#tooltip` slot, which is passed the datum and
// renders unconditionally, and (2) by replaying one mousemove on the next frame
// so the trigger re-reads the wrapper after Vue has rendered it.
//
// The markup below deliberately mirrors the upstream tooltip's own inline
// styles and CSS variables, so a bar tooltip and an area tooltip stay identical
// to look at — this is a repair, not a restyle.

// Attrs are forwarded to <NcBarChart> explicitly below, so they must not also
// land on the wrapping div.
defineOptions({ inheritAttrs: false })

const props = defineProps<{
  // `data`, `height` and `yAxis` are declared rather than left to `$attrs`
  // because <NcBarChart> requires them: a fall-through attr does not satisfy a
  // required prop at the type level, so the build fails without them.
  data: TItem[]
  height: number
  /** Data keys plotted as bars, one per category. */
  yAxis: (keyof TItem)[]
  categories: Record<string, BulletLegendItemInterface>
  /**
   * Which axis the bars run along. Declared because the tooltip's value
   * formatter depends on it — see `valueFormatter`.
   */
  orientation?: Orientation
  xFormatter?: (value: number | Date) => string
  yFormatter?: (value: number | Date) => string
  /** Builds the tooltip title from the hovered row. */
  tooltipTitleFormatter?: (row: any) => string | number
}>()

/**
 * The formatter that turns a series value into tooltip text. @unovis/ts maps the
 * VALUE to the bottom (x) axis when the bars run horizontally and to the left
 * (y) axis when they run vertically, so the two orientations format a value with
 * opposite props — a horizontal chart's `yFormatter` is its index -> label
 * lookup, not its value formatter. Upstream makes the same swap for its own
 * tooltip; getting it wrong here would print a bucket label where a count
 * belongs.
 */
const valueFormatter = computed(() =>
  props.orientation === Orientation.Horizontal ? props.xFormatter : props.yFormatter,
)

/**
 * Unwrap the hovered datum. Stacked bars arrive wrapped by @unovis/ts; grouped
 * and horizontal bars arrive as the row itself. Keyed on `datum` rather than on
 * the chart's own `stacked` prop so it keeps working if upstream ever
 * normalises the two.
 */
function rowOf(values: unknown): Record<string, unknown> | null {
  if (!values || typeof values !== 'object') {
    return null
  }
  const wrapper = values as { datum?: unknown }
  const row = wrapper.datum && typeof wrapper.datum === 'object' ? wrapper.datum : values
  return row as Record<string, unknown>
}

function titleOf(row: Record<string, unknown>): string {
  if (props.tooltipTitleFormatter) {
    return String(props.tooltipTitleFormatter(row))
  }
  const first = Object.values(row)[0]
  return first === undefined ? '' : String(first)
}

/** One row per category that the hovered datum actually carries a number for. */
function seriesOf(row: Record<string, unknown>) {
  return Object.entries(props.categories)
    .map(([key, category]) => ({ key, category, value: row[key] }))
    .filter(entry => typeof entry.value === 'number' && Number.isFinite(entry.value))
    .map(entry => ({
      key: entry.key,
      name: entry.category.name ?? entry.key,
      color: Array.isArray(entry.category.color) ? entry.category.color[0] : entry.category.color,
      text: valueFormatter.value
        ? valueFormatter.value(entry.value as number)
        : String(entry.value),
    }))
}

// See (2) above. Capture phase is required: the upstream handler calls
// `stopPropagation()` as soon as a trigger matches, so a listener on the
// bubbling phase would never run at all.
let replaying = false
function replayMouseMove(event: MouseEvent) {
  if (replaying) {
    return
  }
  const target = event.target
  if (!(target instanceof Element)) {
    return
  }
  replaying = true
  requestAnimationFrame(() => {
    target.dispatchEvent(new MouseEvent('mousemove', {
      bubbles: true,
      clientX: event.clientX,
      clientY: event.clientY,
    }))
    replaying = false
  })
}
</script>

<template>
  <div @mousemove.capture="replayMouseMove">
    <NcBarChart
      v-bind="$attrs"
      :data="data"
      :height="height"
      :y-axis="yAxis"
      :categories="categories"
      :orientation="orientation"
      :x-formatter="xFormatter"
      :y-formatter="yFormatter"
      :tooltip-title-formatter="tooltipTitleFormatter"
    >
      <template #tooltip="{ values }">
        <div v-if="rowOf(values)" style="display: flex; flex-direction: column;">
          <div
            :style="{
              color: 'var(--vis-tooltip-title-color, #000)',
              borderBottom: seriesOf(rowOf(values)!).length
                ? 'var(--vis-tooltip-title-border-bottom, 1px solid #e5e7eb)'
                : 'none',
              padding: 'var(--vis-tooltip-title-padding, 0.75rem 0.75rem 0.5rem 0.75rem)',
              margin: seriesOf(rowOf(values)!).length
                ? 'var(--vis-tooltip-title-margin, 0 0 0.25rem 0)'
                : '0',
              fontSize: 'var(--vis-tooltip-title-font-size, 0.875rem)',
              lineHeight: 'var(--vis-tooltip-title-line-height, 100%)',
              fontWeight: 'var(--vis-tooltip-title-font-weight, 600)',
            }"
          >
            {{ titleOf(rowOf(values)!) }}
          </div>
          <div
            v-if="seriesOf(rowOf(values)!).length"
            style="display: grid; grid-template-columns: auto 1fr auto; align-items: center;
                   gap: var(--vis-tooltip-content-gap, 0.25rem 0.5rem);
                   padding: var(--vis-tooltip-content-padding, 0 0.75rem 0.5rem 0.75rem);"
          >
            <template v-for="entry in seriesOf(rowOf(values)!)" :key="entry.key">
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
    </NcBarChart>
  </div>
</template>
