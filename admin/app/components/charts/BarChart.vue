<script setup lang="ts" generic="TItem extends Record<string, unknown>">
import { Orientation } from '@unovis/ts'
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
//      look intermittent rather than systematic. This one predates the bar
//      conversion (#1218): the horizontal bar charts already had it.
//
// Repairing both here rather than at each call site: (1) by rendering the
// tooltip ourselves through the `#tooltip` slot, which is handed the datum and
// renders unconditionally, and (2) by replaying one mousemove on the next frame
// so the trigger re-reads the wrapper after Vue has rendered it.
//
// Every admin bar chart goes through this component — never <NcBarChart>
// directly, or it gets both bugs back.

// Attrs are forwarded to <NcBarChart> explicitly below, so they must not also
// land on the wrapping div.
defineOptions({ inheritAttrs: false })

defineProps<{
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
   * formatter depends on it — see `BarChartTooltip`.
   */
  orientation?: Orientation
  xFormatter?: (value: number | Date) => string
  yFormatter?: (value: number | Date) => string
  /** Builds the tooltip title from the hovered row. */
  tooltipTitleFormatter?: (row: any) => string | number
}>()

// See (2) above. The capture phase is required, not a preference: the upstream
// handler calls `stopPropagation()` as soon as a trigger matches, so a listener
// on the bubbling phase would never run at all. The guard keeps the replayed
// event — which re-enters this same handler — from scheduling another one.
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
        <ChartsBarChartTooltip
          :values="values"
          :categories="categories"
          :orientation="orientation"
          :x-formatter="xFormatter"
          :y-formatter="yFormatter"
          :title-formatter="tooltipTitleFormatter"
        />
      </template>
    </NcBarChart>
  </div>
</template>
