<script setup lang="ts" generic="TItem extends Record<string, unknown>">
import type { BulletLegendItemInterface } from 'vue-chrts/types'

// Sibling of <ChartsBarChart> for area and line series. Every admin area chart
// goes through it — never <NcAreaChart> directly (#1404), or the chart loses
// four things this wrapper is here to guarantee:
//
//   1. the shared styling, applied here instead of `v-bind`-ed at each call
//      site, so a chart cannot silently drift off the palette;
//   2. a fixed-height container, so a loading or empty chart never collapses
//      the surrounding layout (the pattern from #192);
//   3. `ClientOnly`, because @unovis/ts measures the DOM at mount;
//   4. XML-escaped tick formatters (#842) — a label containing `&`, `<` or `>`
//      otherwise makes upstream's hand-built SVG fragment invalid XML.
//
// The upstream chart is referenced as <NcAreaChart> (see `nuxtCharts.prefix` in
// nuxt.config.ts) so this wrapper does not resolve against its own
// auto-imported name.
//
// Anything not declared below falls through to <NcAreaChart> untouched, which
// is how per-chart props (`xNumTicks`, `stacked`, `hideArea`, `yDomain`) reach
// it. The formatters are declared rather than left to fall through precisely
// because they must be escaped on the way past.

import { computed } from 'vue'
import { CurveType } from 'vue-chrts'
// Imports below are explicit rather than auto-imported: this component is
// mounted directly by the unit tests, which run outside Nuxt and resolve
// neither auto-imported utils nor auto-registered components.
import { CHART_AXIS_TEXT_COLOR, CHART_GUIDE_COLOR, CHART_PRIMARY, defaultSeriesColor } from '~/utils/chart-palette'
import { escapeTickFormatter } from '~/utils/chart-text'
import ChartTooltip from './ChartTooltip.vue'

defineOptions({ inheritAttrs: false })

interface Props {
  data: TItem[]
  categories: Record<string, BulletLegendItemInterface>
  height?: number
  loading?: boolean
  emptyMessage?: string
  // Declared as one signature over `number | Date` rather than vue-chrts'
  // `axisFormatter`, which is a UNION of a number arm and a Date arm: the union
  // is not callable without narrowing, so it cannot be handed to `ChartTooltip`.
  // Same declaration as <ChartsBarChart>, and assignable to `axisFormatter` on
  // the way into the upstream component.
  xFormatter?: (value: number | Date) => string
  yFormatter?: (value: number | Date) => string
  /** Builds the tooltip title from the hovered row; see `ChartTooltip`. */
  tooltipTitleFormatter?: (row: any) => string | number
}

const props = withDefaults(defineProps<Props>(), {
  height: 240,
  loading: false,
  emptyMessage: 'No data available',
})

defineSlots<{
  tooltip?(props: { values: TItem | undefined }): unknown
}>()

/**
 * Past one series, the legend is mandatory rather than optional chrome —
 * identity can no longer be carried by colour alone — and the crosshair goes
 * neutral, because an accent-coloured one belongs to a chart whose only series
 * is the accent and would read as one more series here.
 *
 * Derived from the category count rather than offered as a `multi` prop: an
 * optional BOOLEAN prop cannot express "not specified" — Vue casts an absent
 * one to `false`, so the fallback would never run and every multi-series chart
 * would silently lose its legend. A call site that needs the other behaviour
 * passes `hide-legend` / `crosshair-config`, which land in `$attrs` and are
 * bound after these, so they win.
 */
const isMulti = computed(() => Object.keys(props.categories).length > 1)

const isEmpty = computed(
  () => !props.loading && (props.data?.length ?? 0) === 0,
)

const resolvedCategories = computed(() => {
  const out: Record<string, BulletLegendItemInterface> = {}
  Object.keys(props.categories).forEach((key, i) => {
    const cat = props.categories[key]!
    out[key] = { ...cat, color: cat.color ?? defaultSeriesColor(i) }
  })
  return out
})

const safeXFormatter = computed(() => escapeTickFormatter(props.xFormatter))
const safeYFormatter = computed(() => escapeTickFormatter(props.yFormatter))

/*
 * The fill fades the series colour out downwards. `stopColor` is deliberately
 * absent: NcAreaChart writes each stop's `stop-color` from the category's own
 * colour and ignores anything we pass (verified in vue-chrts@2.2.1
 * AreaChart.js), so pinning one here would only mislead the next reader into
 * thinking every area is rosegold. A function, not a const, so each chart gets
 * its own mutable objects — the component's prop types require that.
 */
function gradientStops() {
  return [
    { offset: '0%', stopOpacity: 0.4 },
    { offset: '100%', stopOpacity: 0 },
  ]
}

// Quiet axes: keep the labels, drop every grid, domain and tick line so the
// series is the only thing drawn with weight.
const axisText = {
  tickTextColor: CHART_AXIS_TEXT_COLOR,
  tickTextFontSize: '11px',
} as const

const crosshairConfig = computed(() => ({
  color: isMulti.value ? CHART_GUIDE_COLOR : CHART_PRIMARY,
  strokeColor: isMulti.value ? CHART_GUIDE_COLOR : CHART_PRIMARY,
  strokeWidth: 1,
}))
</script>

<template>
  <div
    :style="{ height: `${height}px` }"
    class="relative w-full"
  >
    <USkeleton v-if="loading" class="absolute inset-0 size-full" />
    <div
      v-else-if="isEmpty"
      class="absolute inset-0 flex items-center justify-center px-4 text-center text-sm text-muted"
    >
      {{ emptyMessage }}
    </div>
    <ClientOnly v-else>
      <NcAreaChart
        :data="data"
        :categories="resolvedCategories"
        :height="height"
        :x-formatter="safeXFormatter"
        :y-formatter="safeYFormatter"
        :curve-type="CurveType.MonotoneX"
        :line-width="2"
        :gradient-stops="gradientStops()"
        :x-grid-line="false"
        :y-grid-line="false"
        :x-domain-line="false"
        :y-domain-line="false"
        :x-tick-line="false"
        :y-tick-line="false"
        :y-num-ticks="4"
        :x-axis-config="{ ...axisText }"
        :y-axis-config="{ ...axisText }"
        :crosshair-config="crosshairConfig"
        :hide-legend="!isMulti"
        :padding="{ top: 8, right: 8, bottom: 4, left: 8 }"
        v-bind="$attrs"
      >
        <!--
          The tooltip is always ours. Upstream's own renders a light card built
          from `--vis-tooltip-*` fallbacks, and `main.css` has neutralised the
          container those variables style — so leaving it in place would draw
          bare text on nothing. `ChartTooltip` reproduces its semantics on the
          app's surface tokens.
        -->
        <template #tooltip="{ values }">
          <slot
            v-if="$slots.tooltip"
            name="tooltip"
            :values="(values as TItem | undefined)"
          />
          <ChartTooltip
            v-else
            :values="values"
            :categories="resolvedCategories"
            :y-formatter="yFormatter"
            :title-formatter="tooltipTitleFormatter"
          />
        </template>
      </NcAreaChart>
      <template #fallback>
        <USkeleton class="absolute inset-0 size-full" />
      </template>
    </ClientOnly>
  </div>
</template>
