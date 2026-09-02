<script setup lang="ts" generic="TItem extends Record<string, unknown>">
import { Orientation } from '@unovis/ts'
import { computed, onBeforeUnmount } from 'vue'
import type { BulletLegendItemInterface } from 'vue-chrts/types'
// Imports below are explicit rather than auto-imported: this component is
// mounted directly by the unit tests, which run outside Nuxt and resolve
// neither auto-imported utils nor auto-registered components.
import { escapeTickFormatter } from '~/utils/chart-text'
import ChartTooltip from './ChartTooltip.vue'

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
   * formatter depends on it — see `ChartTooltip`.
   */
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
  tooltipTitleFormatter?: (row: any) => string | number
}>()

// See (2) above. The capture phase is required, not a preference: the upstream
// handler calls `stopPropagation()` as soon as a trigger matches, so a listener
// on the bubbling phase would never run at all.
//
// What gets replayed is the LATEST pointer position, not the one that scheduled
// the frame. A pointer crossing several bars inside a single frame would
// otherwise be re-announced at the first bar it touched, painting that bar's
// values next to a cursor that has already moved on — a wrong tooltip, which is
// worse than the empty one this repairs.
//
// `dispatching` is what keeps the replayed event — which re-enters this very
// handler — from scheduling a replay of its own. It is set only around the
// dispatch, which is synchronous, so a real mousemove arriving inside the frame
// still updates `pendingMove` instead of being swallowed. (`event.isTrusted`
// would read as the obvious discriminator and is not one: every event a test
// dispatches is untrusted too.)
let pendingMove: MouseEvent | null = null
let frameHandle: number | null = null
let dispatching = false
function replayMouseMove(event: MouseEvent) {
  if (dispatching) {
    return
  }
  pendingMove = event
  if (frameHandle !== null) {
    return
  }
  frameHandle = requestAnimationFrame(() => {
    frameHandle = null
    const latest = pendingMove
    pendingMove = null
    if (!latest || !(latest.target instanceof Element)) {
      return
    }
    dispatching = true
    try {
      latest.target.dispatchEvent(new MouseEvent('mousemove', {
        bubbles: true,
        clientX: latest.clientX,
        clientY: latest.clientY,
      }))
    }
    finally {
      dispatching = false
    }
  })
}

/**
 * Drop a frame that has not run yet. Needed in two places.
 *
 * On MOUSELEAVE it is a correctness fix, not tidiness: a pointer that leaves
 * within a frame of its last move would have the tooltip hidden by upstream's
 * own `mouseleave`, and then re-shown by our replay a few milliseconds later —
 * stuck open over a chart the pointer has left, with no further event coming to
 * hide it again.
 *
 * On UNMOUNT it is tidiness: the dispatch would find no listeners in a detached
 * tree, but holding the event and its element until the frame runs is pointless.
 */
function cancelReplay() {
  if (frameHandle !== null) {
    cancelAnimationFrame(frameHandle)
    frameHandle = null
  }
  pendingMove = null
}

onBeforeUnmount(cancelReplay)

// Tick text is string-interpolated into an SVG fragment and parsed as strict
// XML by @unovis/ts, so `&`/`<`/`>` in a label must be escaped before it reaches
// an AXIS (#842) — champion names such as "Nunu & Willump" are the live case.
// The tooltip keeps the RAW formatters: its text goes through Vue
// interpolation, which escapes on its own, and feeding it pre-escaped text
// would print the entity itself.
const safeXFormatter = computed(() => escapeTickFormatter(props.xFormatter))
const safeYFormatter = computed(() => escapeTickFormatter(props.yFormatter))
</script>

<template>
  <div
    @mousemove.capture="replayMouseMove"
    @mouseleave="cancelReplay"
  >
    <NcBarChart
      v-bind="$attrs"
      :data="data"
      :height="height"
      :y-axis="yAxis"
      :categories="categories"
      :orientation="orientation"
      :x-formatter="safeXFormatter"
      :y-formatter="safeYFormatter"
      :tooltip-title-formatter="tooltipTitleFormatter"
    >
      <template #tooltip="{ values }">
        <ChartTooltip
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
