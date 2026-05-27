<script setup lang="ts" generic="TItem extends Record<string, unknown>">
import type { BulletLegendItemInterface } from 'vue-chrts/types'
import type { CurveType, LegendPosition } from 'vue-chrts/enums'

// Sibling of <ChartsLineChart>. Same defaults — emerald palette, fixed
// container, decoupled API — but renders the filled area beneath the
// line. Exposes <c>gradientStops</c> so callers can flatten or fade the
// fill without dropping back to the upstream component directly. The
// upstream chart is referenced as <NcAreaChart> (see `nuxtCharts.prefix`
// in nuxt.config.ts) to avoid self-resolving against this wrapper's
// own auto-imported name.

interface Props {
  data: TItem[]
  categories: Record<string, BulletLegendItemInterface>
  height?: number
  loading?: boolean
  emptyMessage?: string
  xLabel?: string
  yLabel?: string
  xFormatter?: (tick: number | Date, i: number, ticks: (number | Date)[]) => string
  yFormatter?: (tick: number | Date, i: number, ticks: (number | Date)[]) => string
  curveType?: CurveType
  lineWidth?: number
  yGridLine?: boolean
  yDomain?: [number | undefined, number | undefined]
  hideLegend?: boolean
  hideXAxis?: boolean
  hideYAxis?: boolean
  legendPosition?: LegendPosition
  gradientStops?: Array<{ offset: string, stopOpacity: number }>
}

const props = withDefaults(defineProps<Props>(), {
  height: 240,
  loading: false,
  emptyMessage: 'No data available',
  lineWidth: 2,
  yGridLine: true,
})

defineSlots<{
  tooltip?(props: { values: TItem | undefined }): unknown
}>()

const resolvedCategories = computed(() => {
  const out: Record<string, BulletLegendItemInterface> = {}
  Object.keys(props.categories).forEach((key, i) => {
    const cat = props.categories[key]!
    out[key] = { ...cat, color: cat.color ?? defaultSeriesColor(i) }
  })
  return out
})

const isEmpty = computed(
  () => !props.loading && (props.data?.length ?? 0) === 0,
)

const crosshairConfig = { color: CHART_GUIDE_COLOR }
</script>

<template>
  <div
    :style="{ height: `${height}px` }"
    class="relative w-full"
  >
    <USkeleton v-if="loading" class="absolute inset-0 size-full" />
    <div
      v-else-if="isEmpty"
      class="absolute inset-0 flex items-center justify-center text-sm text-muted"
    >
      {{ emptyMessage }}
    </div>
    <ClientOnly v-else>
      <NcAreaChart
        :data="data"
        :categories="resolvedCategories"
        :height="height"
        :x-label="xLabel"
        :y-label="yLabel"
        :x-formatter="xFormatter"
        :y-formatter="yFormatter"
        :curve-type="curveType"
        :line-width="lineWidth"
        :y-grid-line="yGridLine"
        :y-domain="yDomain"
        :hide-legend="hideLegend"
        :hide-x-axis="hideXAxis"
        :hide-y-axis="hideYAxis"
        :legend-position="legendPosition"
        :gradient-stops="gradientStops"
        :crosshair-config="crosshairConfig"
      >
        <template v-if="$slots.tooltip" #tooltip="{ values }">
          <slot name="tooltip" :values="(values as TItem | undefined)" />
        </template>
      </NcAreaChart>
      <template #fallback>
        <USkeleton class="absolute inset-0 size-full" />
      </template>
    </ClientOnly>
  </div>
</template>
